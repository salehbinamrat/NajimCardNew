using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// The authenticated RouterOS session offers two independent read lanes:
/// fast Hotspot/PPP queries and the heavier User Manager inventory.
/// The name scan must overlap these reads but remain fail-closed.
/// No router mutation is performed by these tests.
/// </summary>
public sealed class VoucherPreflightParallelReadTests
{
    private sealed class Reader(
        Func<string,CancellationToken,
            Task<IReadOnlyList<IReadOnlyDictionary<string,string>>>> read)
        : IRouterCommandClient
    {
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default) => read(command,cancellationToken);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string,string>> Row(
        string field,string value) =>
        [new Dictionary<string,string> { [field]=value }];

    [Fact]
    public async Task IndependentUserManagerInventoryOverlapsHotspotPreflightRead()
    {
        using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var inventoryEntered=new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fast=new Reader(async (command,token)=>
        {
            if(command=="/ip/hotspot/user/print")
            {
                // Sequential scanning would deadlock here. Starting User
                // Manager on the independent inventory lane releases this.
                await inventoryEntered.Task.WaitAsync(token);
                return Row("name","hs01");
            }
            if(command=="/ppp/secret/print")
                return Row("name","pp01");
            throw new InvalidOperationException("Unexpected fast lane command");
        });
        var inventory=new Reader((command,_)=>
        {
            if(command!="/tool/user-manager/user/print")
                throw new InvalidOperationException("Unexpected inventory lane command");
            inventoryEntered.TrySetResult(true);
            return Task.FromResult(Row("username","um01"));
        });
        await using var session=new ResilientRouterSession(fast,
            _=>Task.FromResult<IRouterCommandClient>(inventory));
        var names=await new RouterGateway(session)
            .ReadExistingVoucherNamesAsync(limit.Token);

        Assert.Equal(3,names.Count);
        Assert.Contains("hs01",names);
        Assert.Contains("pp01",names);
        Assert.Contains("um01",names);
    }

    [Fact]
    public async Task PermissionErrorInParallelInventoryNeverReturnsPartialSafeNames()
    {
        using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fast=new Reader((command,_)=>
            Task.FromResult(Row("name",command.Contains("ppp")?"pp01":"hs01")));
        var inventory=new Reader((_,_)=>
            Task.FromException<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                new RouterOsTrapException("not enough permissions")));
        await using var session=new ResilientRouterSession(fast,
            _=>Task.FromResult<IRouterCommandClient>(inventory));
        await Assert.ThrowsAsync<RouterOsTrapException>(()=>
            new RouterGateway(session).ReadExistingVoucherNamesAsync(limit.Token));
    }
}
