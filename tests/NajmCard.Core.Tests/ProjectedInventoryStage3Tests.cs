using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class ProjectedInventoryStage3Tests
{
    private sealed class Router : IRouterCommandClient
    {
        public readonly List<IReadOnlyDictionary<string,string>?> Calls=[];
        public bool Empty,MissingCounter,ProjectionUnsupported;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            Calls.Add(attributes);Assert.Equal("/ip/hotspot/user/print",command);
            var row=new Dictionary<string,string>{[".id"]="*A",["name"]="card",["profile"]="gold",
                ["disabled"]="no",["bytes-in"]="20",["bytes-out"]="30",["limit-bytes-total"]="100"};
            if(MissingCounter||ProjectionUnsupported&&attributes is not null)row.Remove("bytes-in");
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(Empty?[]:[row]);
        }
    }

    [Fact]
    public async Task HotspotMissingCounterIsNotCertifiedEvenAfterFullRead()
    {
        var router=new Router{MissingCounter=true};
        await Assert.ThrowsAsync<InvalidDataException>(()=>new RouterGateway(router).ReadUsersAsync());
        Assert.Equal(2,router.Calls.Count);
    }
    [Fact]
    public async Task EmptyHotspotListIsOneSuccessfulProjectedRequest()
    {
        var router=new Router{Empty=true};
        Assert.Empty(await new RouterGateway(router).ReadUsersAsync());Assert.Single(router.Calls);
    }
    [Fact]
    public async Task UnsupportedProjectionIsLearnedOnlyAfterVerifiedReadback()
    {
        var router=new Router{ProjectionUnsupported=true};var gateway=new RouterGateway(router);
        Assert.Equal(50,Assert.Single(await gateway.ReadUsersAsync()).ConsumedBytes);
        Assert.Equal(2,router.Calls.Count);
        await gateway.ReadUsersAsync();Assert.Equal(3,router.Calls.Count);Assert.Null(router.Calls[2]);
    }
}
