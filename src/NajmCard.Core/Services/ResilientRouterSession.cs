using System.IO;
using System.Net.Sockets;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// One authenticated RouterOS session per login, with independent fast and
/// User Manager inventory lanes. Transport failures can safely reconnect
/// reads once; mutations are NEVER replayed when an outcome is unknown.
/// No automatic connection is made after explicit logout.
/// </summary>
public sealed class ResilientRouterSession : IRouterCommandClient, IRouterReceiptClient,
    IRouterQueryClient, IRouterNameQueryClient, IRouterSessionQueryClient, IAsyncDisposable
{
    private sealed class Lane(IRouterCommandClient? current=null)
    {
        internal readonly SemaphoreSlim Gate=new(1,1);
        internal IRouterCommandClient? Current=current;
        internal string? VerifiedIdentity;
    }

    private readonly Lane _fast;
    private readonly Lane _inventory=new();
    private readonly Func<CancellationToken,Task<IRouterCommandClient>> _connect;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly CancellationToken _sessionToken;
    private int _closed;
    private string? _routerIdentity;

    public Task<string> BindRouterIdentityAsync(CancellationToken ct=default)=>
        ExecuteAsync(_fast,async (current,token)=>
        {
            var identity=await RouterDeviceIdentity.ReadAsync(current,token);
            var previous=Interlocked.CompareExchange(ref _routerIdentity,identity,null);
            if(previous is not null&&previous!=identity)throw new RouterIdentityMismatchException();
            _fast.VerifiedIdentity=identity;
            return identity;
        },true,ct);

    private async Task VerifyLaneIdentityAsync(Lane lane,IRouterCommandClient current,CancellationToken ct)
    {
        var expected=Volatile.Read(ref _routerIdentity);
        if(expected is null||lane.VerifiedIdentity==expected)return;
        if(await RouterDeviceIdentity.ReadAsync(current,ct)!=expected)
            throw new RouterIdentityMismatchException();
        lane.VerifiedIdentity=expected;
    }

    public ResilientRouterSession(IRouterCommandClient authenticated,
        Func<CancellationToken,Task<IRouterCommandClient>> connect)
    {
        ArgumentNullException.ThrowIfNull(authenticated);
        _connect=connect??throw new ArgumentNullException(nameof(connect));
        _fast=new Lane(authenticated);
        _sessionToken=_lifetime.Token;
    }

    private static bool IsReadOnly(string command)=>
        command.EndsWith("/print",StringComparison.Ordinal);

    private Lane For(string command,IReadOnlyDictionary<string,string>? attributes=null)=>
        attributes?.ContainsKey("count-only")==true?_fast:
        command is "/tool/user-manager/user/print" or "/user-manager/user/print"
            or "/tool/user-manager/session/print" or "/user-manager/session/print"
                ?_inventory:_fast;

    private static bool Disconnected(IRouterCommandClient current,Exception error)=>
        current is RouterOsClient { IsUsable:false } ||
        error is ObjectDisposedException or OperationCanceledException or SocketException or InvalidDataException ||
        error is IOException and not RouterOsTrapException;

    private static async Task ReleaseAsync(IRouterCommandClient? old)
    {
        if(old is IAsyncDisposable disposable)
        {
            try{await disposable.DisposeAsync();}
            catch(IOException){}
            catch(ObjectDisposedException){}
        }
    }

    private async Task<T> ExecuteAsync<T>(Lane lane,
        Func<IRouterCommandClient,CancellationToken,Task<T>> operation,
        bool retryRead,CancellationToken ct)
    {
        if(Volatile.Read(ref _closed)!=0)
            throw new ObjectDisposedException(nameof(ResilientRouterSession),
                "انتهت جلسة الراوتر، سجل الدخول مجددا.");
        using var request=CancellationTokenSource.CreateLinkedTokenSource(ct,_sessionToken);
        var token=request.Token;
        await lane.Gate.WaitAsync(token);
        try
        {
            var current=lane.Current ??= await _connect(token);
            token.ThrowIfCancellationRequested();
            try
            {
                await VerifyLaneIdentityAsync(lane,current,token);
                return await operation(current,token);
            }
            catch(Exception ex) when(Disconnected(current,ex))
            {
                lane.Current=null;
                lane.VerifiedIdentity=null;
                await ReleaseAsync(current);
                // A write may already have reached the router before the reply was lost.
                if(!retryRead || token.IsCancellationRequested)
                    throw;
                var replacement=lane.Current=await _connect(token);
                token.ThrowIfCancellationRequested();
                try
                {
                    await VerifyLaneIdentityAsync(lane,replacement,token);
                    return await operation(replacement,token);
                }
                catch(Exception repeat) when(Disconnected(replacement,repeat))
                {
                    lane.Current=null;
                    lane.VerifiedIdentity=null;
                    await ReleaseAsync(replacement);
                    throw;
                }
            }
        }
        finally{lane.Gate.Release();}
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
        string command,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default)
    {
        RouterOsCommandPolicy.Validate(command,attributes);
        return ExecuteAsync(For(command,attributes),
            (current,ct)=>current.CommandAsync(command,attributes,ct),
            IsReadOnly(command),cancellationToken);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> ReadInventoryAsync(
        string command,IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
    {
        RouterOsCommandPolicy.Validate(command,attributes);
        if(!IsReadOnly(command))throw new ArgumentException("Inventory lane accepts reads only",nameof(command));
        return ExecuteAsync(_inventory,(current,ct)=>current.CommandAsync(command,attributes,ct),true,cancellationToken);
    }

    public Task<RouterCommandReply> CommandWithReceiptAsync(
        string command,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default)
    {
        RouterOsCommandPolicy.Validate(command,attributes);
        return ExecuteAsync(For(command,attributes),(current,ct)=>
            current is IRouterReceiptClient receipt
                ?receipt.CommandWithReceiptAsync(command,attributes,ct)
                :throw new InvalidOperationException(
                    "الاتصال لا يدعم التحقق من رد RouterOS."),
            IsReadOnly(command),cancellationToken);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
        string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default) =>
        // Exact-ID readback is a bounded one-record request issued after
        // add/activate on this SAME fast lane. Large User Manager inventory
        // scans stay on their separate lane, so confirming a voucher cannot
        // queue behind thousands of unrelated users.
        ExecuteAsync(_fast,(current,ct)=>
            current is IRouterQueryClient filtered
                ?filtered.QueryByIdAsync(command,id,attributes,ct)
                :throw new InvalidOperationException(
                    "الاتصال لا يدعم قراءة السجل المحدد."),
            true,cancellationToken);

    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByNameAsync(
        string command,string name,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default)
    {
        RouterOsCommandPolicy.Validate(command,attributes);
        return ExecuteAsync(_fast,(current,ct)=>
            current is IRouterNameQueryClient query
                ?query.QueryByNameAsync(command,name,attributes,ct)
                :throw new NotSupportedException(
                    "الاتصال لا يدعم استعلام اسم الحساب بشكل مقيّد."),true,cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _closed,1)!=0)return;
        _lifetime.Cancel();
        try { await Task.WhenAll(CloseLaneAsync(_fast),CloseLaneAsync(_inventory)); }
        finally { _lifetime.Dispose(); }
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QuerySessionsForAccountAsync(
        string command,string username,string? internalId=null,
        IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        =>ExecuteAsync(_inventory,(current,ct)=>current is IRouterSessionQueryClient query
            ?query.QuerySessionsForAccountAsync(command,username,internalId,attributes,ct)
            :throw new NotSupportedException("Filtered session query is unavailable"),true,cancellationToken);

    private static async Task CloseLaneAsync(Lane lane)
    {
        // Wait for commands to release the transport before disposing its lock.
        await lane.Gate.WaitAsync();
        try
        {
            var client=lane.Current;
            lane.Current=null;
            await ReleaseAsync(client);
        }
        finally { lane.Gate.Release(); }
    }
}
