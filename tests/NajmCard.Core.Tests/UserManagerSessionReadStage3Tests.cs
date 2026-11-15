using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerSessionReadStage3Tests
{
    private sealed class Router(int size=10000) : IRouterCommandClient,IRouterSessionQueryClient
    {
        public int FullReads,Queries,Returned;
        public bool IgnoreFilter,MissingProjectionIdentity;
        public string? Trap;
        public TaskCompletionSource? Started,Release;
        private IEnumerable<IReadOnlyDictionary<string,string>> Rows=>Enumerable.Range(0,size).Select(i=>
            (IReadOnlyDictionary<string,string>)new Dictionary<string,string>{[".id"]="*"+(i+1).ToString("X"),
                ["username"]=i==0?"card":"other"+i,["active"]="no",["start-time"]="2026-11-15 10:00:00"});
        private IReadOnlyList<IReadOnlyDictionary<string,string>> Project(IEnumerable<IReadOnlyDictionary<string,string>> rows,
            IReadOnlyDictionary<string,string>? attrs)
        {
            var fields=attrs?.GetValueOrDefault(".proplist")?.Split(',').ToHashSet();
            var result=rows.Select(r=>(IReadOnlyDictionary<string,string>)r.Where(p=>
                fields is null||fields.Contains(p.Key)&&!(MissingProjectionIdentity&&p.Key=="username")).ToDictionary()).ToArray();
            Returned+=result.Length;return result;
        }
        public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            if(command=="/system/resource/print")return [new Dictionary<string,string>{{"version","6.49.19"}}];
            Assert.Equal("/tool/user-manager/session/print",command);FullReads++;
            Started?.TrySetResult();if(Release is not null)await Release.Task.WaitAsync(cancellationToken);
            return Project(Rows,attributes);
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QuerySessionsForAccountAsync(string command,
            string username,string? internalId=null,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Queries++;if(Trap is not null)throw new RouterOsTrapException(Trap);
            return Task.FromResult(Project(IgnoreFilter?Rows:Rows.Where(r=>r["username"]==username||r["username"]==internalId),attributes));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public async Task AccountDetailsTransferOnlyMatchingRows(int size)
    {
        var router=new Router(size);var gateway=new RouterGateway(router);
        var result=await gateway.ReadUmSessionsForAccountAsync("card","*F");
        Assert.Single(result.Rows);Assert.Equal(0,router.FullReads);Assert.Equal(1,router.Returned);
        Assert.Equal(1,router.Queries);Assert.NotNull(result.Rows[0].StartedAt);
        Assert.Empty((await gateway.ReadUmSessionsForAccountAsync("absent")).Rows);
        Assert.Equal(0,router.FullReads);Assert.Equal(2,router.Queries);
    }

    [Fact]
    public async Task UnsupportedQueryLearnsCapabilityAndSharesOneFullSnapshotIncludingEmpty()
    {
        var router=new Router{Trap="query not supported"};var gateway=new RouterGateway(router);
        Assert.Single((await gateway.ReadUmSessionsForAccountAsync("card")).Rows);
        var empty=await gateway.ReadUmSessionsForAccountAsync("absent");Assert.Empty(empty.Rows);
        Assert.True(empty.IsFromCache);Assert.Equal(1,router.FullReads);Assert.Equal(1,router.Queries);
    }

    [Fact]
    public async Task PermissionDeniedDoesNotTriggerBroaderReadOrInventZero()
    {
        var router=new Router{Trap="not enough permissions"};
        await Assert.ThrowsAsync<RouterOsTrapException>(()=>new RouterGateway(router).ReadUmSessionsForAccountAsync("card"));
        Assert.Equal(0,router.FullReads);Assert.Equal(1,router.Queries);
    }

    [Fact]
    public async Task IgnoredFilterCannotExposeOtherAccountsAndIsNotRetriedForEverySelection()
    {
        var router=new Router{IgnoreFilter=true};var gateway=new RouterGateway(router);
        Assert.Single((await gateway.ReadUmSessionsForAccountAsync("card")).Rows);
        Assert.Empty((await gateway.ReadUmSessionsForAccountAsync("absent")).Rows);
        Assert.Equal(1,router.Queries);Assert.Equal(1,router.FullReads);
    }

    [Fact]
    public async Task ProjectionFallbackKeepsServerFilterAndLearnsTheUnsupportedProjection()
    {
        var router=new Router{MissingProjectionIdentity=true};var gateway=new RouterGateway(router);
        Assert.Single((await gateway.ReadUmSessionsForAccountAsync("card")).Rows);
        Assert.Equal(2,router.Queries);
        Assert.Single((await gateway.ReadUmSessionsForAccountAsync("card")).Rows);
        Assert.Equal(3,router.Queries);Assert.Equal(0,router.FullReads);
    }

    [Fact]
    public async Task ConcurrentExplicitRefreshesShareOneCompleteReadWithOriginalTimestamp()
    {
        var router=new Router(100){Started=new(TaskCreationOptions.RunContinuationsAsynchronously),
            Release=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var gateway=new RouterGateway(router);var first=gateway.ReadUmSessionsAsync();
        await router.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second=gateway.ReadUmSessionsAsync();router.Release.SetResult();
        var result=await Task.WhenAll(first,second);
        Assert.Equal(1,router.FullReads);Assert.Same(result[0].Rows,result[1].Rows);
        Assert.False(result[0].IsFromCache);Assert.True(result[1].IsFromCache);
        Assert.Equal(result[0].ReadAt,result[1].ReadAt);
        await gateway.ReadUmSessionsAsync();Assert.Equal(2,router.FullReads);
    }
}
