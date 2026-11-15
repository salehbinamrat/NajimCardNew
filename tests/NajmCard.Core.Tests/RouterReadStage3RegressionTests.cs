using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class RouterReadStage3RegressionTests
{
    private sealed class Sessions : IRouterCommandClient
    {
        public int Reads;
        public bool Empty;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            if(command=="/system/resource/print")return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                [new Dictionary<string,string>{{"version","6.49.19"}}]);
            Assert.Equal("/tool/user-manager/session/print",command);
            Reads++;
            var row=new Dictionary<string,string>{[".id"]="*A",["username"]="card",
                ["start-time"]="2026-11-15 10:00:00",["end-time"]="2026-11-15 10:20:00",
                ["session-time"]="20m",["session-status"]="stopped",["address"]="10.0.0.2"};
            var fields=attributes?.GetValueOrDefault(".proplist")?.Split(',').ToHashSet();
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(Empty?[]:
                [row.Where(p=>fields is null||fields.Contains(p.Key)).ToDictionary()]);
        }
    }

    [Fact]
    public async Task SessionProjectionIncludesEveryAcceptedLegacyAlias()
    {
        var router=new Sessions();
        var row=Assert.Single((await new RouterGateway(router).ReadUmSessionsAsync()).Rows);
        Assert.Equal("2026-11-15 10:00:00",row.Started);
        Assert.Equal("2026-11-15 10:20:00",row.Ended);
        Assert.Equal("20m",row.Uptime);
        Assert.Equal("stopped",row.RawStatus);
        Assert.Equal("10.0.0.2",row.Address);
        Assert.NotNull(row.StartedAt);
        Assert.Null(row.DownloadBytes);
        Assert.Null(row.IsActive);
        Assert.Equal(1,router.Reads);
    }

    [Fact]
    public async Task VerifiedEmptyHistoryDoesNotTriggerAnotherFullDownload()
    {
        var router=new Sessions{Empty=true};var gateway=new RouterGateway(router);
        Assert.Empty((await gateway.ReadUmSessionsForAccountAsync("new-card")).Rows);
        Assert.Empty((await gateway.ReadUmSessionsForAccountAsync("new-card")).Rows);
        Assert.Equal(1,router.Reads);
    }

    private sealed class Inventory : IRouterCommandClient,IRouterQueryClient
    {
        public int Reads;
        public bool Disabled;
        public TaskCompletionSource? Started,Release;
        public TaskCompletionSource? WriteStarted,WriteRelease;
        public bool LoseWriteReply;
        private Dictionary<string,string> User()=>new(){[".id"]="*A",["username"]="card",
            ["actual-profile"]="gold",["customer"]="alice",["disabled"]=Disabled?"yes":"no",
            ["incomplete"]="no",["download-used"]="0",["upload-used"]="0"};
        public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            if(command=="/tool/user-manager/user/print")
            {
                Reads++;var rows=new IReadOnlyDictionary<string,string>[]{User()};
                Started?.TrySetResult();if(Release is not null)await Release.Task.WaitAsync(cancellationToken);
                return rows;
            }
            if(command=="/tool/user-manager/user/set")
            {
                WriteStarted?.TrySetResult();if(WriteRelease is not null)await WriteRelease.Task.WaitAsync(cancellationToken);
                var result=Set(attributes!);
                if(LoseWriteReply)throw new IOException("Lost acknowledgement");
                return result;
            }
            return command switch
            {
                "/tool/user-manager/profile/print"=>[new Dictionary<string,string>{[".id"]="*1",["name"]="gold",["owner"]="alice"}],
                "/tool/user-manager/profile/limitation/print"=>[],
                "/tool/user-manager/profile/profile-limitation/print"=>[],
                "/ip/hotspot/user/print"=>[new Dictionary<string,string>{[".id"]="*A",["name"]="card",["profile"]="default",["disabled"]="no"}],
                _=>throw new InvalidOperationException(command)
            };
        }
        private IReadOnlyList<IReadOnlyDictionary<string,string>> Set(IReadOnlyDictionary<string,string> attrs)
        {Disabled=attrs["disabled"]=="yes";return [];}
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(string command,string id,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
            =>Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([User()]);
    }

    [Fact]
    public async Task MutationInvalidatesPreviouslyCachedInventory()
    {
        var router=new Inventory();var gateway=new RouterGateway(router);
        var first=await gateway.ReadCachedUserManagerV6InventoryAsync();Assert.True(first.IsComplete,first.Diagnostic);
        var result=await gateway.ChangeUserManagerV6UserAsync(Assert.Single(first.Users),"disable");
        Assert.True(result.Success);
        var fresh=await gateway.ReadCachedUserManagerV6InventoryAsync();
        Assert.True(Assert.Single(fresh.Users).Disabled);
        Assert.Equal(2,router.Reads);
    }

    [Fact]
    public async Task InvalidationDuringReadCannotResurrectStaleInventory()
    {
        var router=new Inventory{Started=new(TaskCreationOptions.RunContinuationsAsynchronously),
            Release=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var gateway=new RouterGateway(router);
        var pending=gateway.ReadCachedUserManagerV6InventoryAsync();
        await router.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        gateway.InvalidateUserManagerV6LifecycleCache();router.Release.SetResult();await pending;
        await gateway.ReadCachedUserManagerV6InventoryAsync();
        Assert.Equal(2,router.Reads);
    }

    [Fact]
    public async Task ReadDuringMutationIsNotCertifiedOrCached()
    {
        var router=new Inventory{WriteStarted=new(TaskCreationOptions.RunContinuationsAsynchronously),
            WriteRelease=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var gateway=new RouterGateway(router);
        var old=await gateway.ReadCachedUserManagerV6InventoryAsync();
        var write=gateway.ChangeUserManagerV6UserAsync(Assert.Single(old.Users),"disable");
        await router.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try{Assert.False((await gateway.ReadCachedUserManagerV6InventoryAsync()).IsComplete);}
        finally{router.WriteRelease.SetResult();await write;}
        var fresh=await gateway.ReadCachedUserManagerV6InventoryAsync();
        Assert.True(fresh.IsComplete);Assert.True(Assert.Single(fresh.Users).Disabled);
        Assert.Equal(3,router.Reads);
    }

    [Fact]
    public async Task UncertainWriteAlsoInvalidatesCachedStateWithoutReplayingTheWrite()
    {
        var router=new Inventory{LoseWriteReply=true};var gateway=new RouterGateway(router);
        var old=await gateway.ReadCachedUserManagerV6InventoryAsync();
        Assert.True((await gateway.ChangeUserManagerV6UserAsync(Assert.Single(old.Users),"disable")).UncertainOutcome);
        var fresh=await gateway.ReadCachedUserManagerV6InventoryAsync();
        Assert.True(Assert.Single(fresh.Users).Disabled);Assert.Equal(2,router.Reads);
    }

    [Fact]
    public async Task MissingHotspotCountersCannotBecomeTrustedZeroAfterFallback()
    {
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            new RouterGateway(new Inventory()).ReadDashboardUsersAsync());
    }

    [Fact]
    public async Task CancelledHiddenPageDoesNotSendItsQueuedHeavyRead()
    {
        var router=new Inventory{Started=new(TaskCreationOptions.RunContinuationsAsynchronously),
            Release=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var gateway=new RouterGateway(router);
        var inventory=gateway.ReadUserManagerV6InventoryAsync();
        await router.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var hidden=new CancellationTokenSource();
        var cards=gateway.ReadUsersAsync(hidden.Token);hidden.Cancel();
        try{await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>cards);}
        finally{router.Release.SetResult();await inventory;}
        Assert.Equal(1,router.Reads);
    }

    [Fact]
    public async Task MalformedUtf8IsAConnectionInvalidatingProtocolError()
    {
        using var bytes=new MemoryStream([2,0xC3,0x28,0]);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>
            await RouterOsProtocol.ReadSentenceAsync(bytes,default));
    }

    private sealed class Lane : IRouterCommandClient,IRouterReceiptClient
    {
        public TaskCompletionSource? Block;
        public TaskCompletionSource? Started;
        public string? BlockCommand;
        public int Counts;
        public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            if(Block is not null&&(BlockCommand is null||BlockCommand==command))
            {Started?.TrySetResult();await Block.Task.WaitAsync(cancellationToken);}
            return [];
        }
        public Task<RouterCommandReply> CommandWithReceiptAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {Counts++;return Task.FromResult(new RouterCommandReply([],new Dictionary<string,string>{{"ret","0"}}));}
    }

    [Fact]
    public async Task CountOnlyDoesNotQueueBehindBulkUserManagerRead()
    {
        var fast=new Lane();var bulk=new Lane{Block=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new ResilientRouterSession(fast,_=>Task.FromResult<IRouterCommandClient>(bulk));
        var scan=session.CommandAsync("/tool/user-manager/user/print");
        try
        {
            await session.CommandWithReceiptAsync("/tool/user-manager/user/print",
                new Dictionary<string,string>{{"count-only",""}}).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1,fast.Counts);
        }
        finally{bulk.Block.SetResult();await scan;}
    }

    [Fact]
    public async Task HotspotInventoryAlsoLeavesTelemetryLaneAvailable()
    {
        var block=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast=new Lane{Block=block,Started=started,BlockCommand="/ip/hotspot/user/print"};
        var bulk=new Lane{Block=block,Started=started,BlockCommand="/ip/hotspot/user/print"};
        await using var session=new ResilientRouterSession(fast,_=>Task.FromResult<IRouterCommandClient>(bulk));
        var read=new RouterGateway(session).ReadUsersAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try{await session.CommandAsync("/system/resource/print").WaitAsync(TimeSpan.FromSeconds(2));}
        finally{block.SetResult();await read;}
    }
}
