using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Hotspot active/RADIUS classification without a live router.
/// All three connected tabs consume one read-only source, not historical UM sessions.</summary>
public sealed class LiveHotspotConnectedGroupTests
{
    private sealed class FakeRouter : IRouterCommandClient
    {
        public int Commands { get; private set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Assert.Equal("/ip/hotspot/active/print",command);
            Assert.Null(attributes);
            Commands++;
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=
            [
                new Dictionary<string,string>
                {
                    [".id"]="*01",["user"]="um-001",["radius"]="yes",
                    ["address"]="10.0.1.2",["mac-address"]="AA:BB:CC:DD:EE:01",
                    ["uptime"]="10m",["server"]="hs1",["login-by"]="http-chap"
                },
                new Dictionary<string,string>
                {
                    [".id"]="*02",["user"]="local-002",["radius"]="no",
                    ["address"]="10.0.1.3",["mac-address"]="AA:BB:CC:DD:EE:02",
                    ["uptime"]="3m",["server"]="hs1",["login-by"]="http-pap"
                },
                new Dictionary<string,string>
                {
                    [".id"]="*03",["user"]="local-003",
                    ["address"]="10.0.1.4",["uptime"]="1m"
                },
                new Dictionary<string,string>
                {
                    [".id"]="*04",["user"]="um-004",["flags"]="R",
                    ["address"]="10.0.1.5",["uptime"]="7m"
                },
                new Dictionary<string,string>
                {
                    [".id"]="*05",["user"]="indeterminate",["radius"]="?",
                    ["address"]="10.0.1.6",["uptime"]="7m"
                }
            ];
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task All_UM_Radius_And_Local_Hotspot_Are_Disjoint_Sets()
    {
        var api=new FakeRouter();
        var rows=await new RouterGateway(api).ReadLiveHotspotConnectionsAsync();
        Assert.Equal(5,rows.Count);
        Assert.Equal(2,rows.Count(x=>x.Origin==HotspotConnectionOrigin.UserManagerRadius));
        Assert.Equal(2,rows.Count(x=>x.Origin==HotspotConnectionOrigin.LocalHotspot));
        Assert.Single(rows.Where(x=>x.Origin==HotspotConnectionOrigin.Unknown));
        Assert.Equal(1,api.Commands);
        Assert.Equal("10.0.1.2",rows[0].Address);
        Assert.Equal("AA:BB:CC:DD:EE:01",rows[0].MacAddress);
        Assert.Equal("hs1",rows[0].Server);
    }

    [Fact]
    public async Task Source_Labels_Are_Not_False_Historical_Session_Counts()
    {
        var rows=await new RouterGateway(new FakeRouter()).ReadLiveHotspotConnectionsAsync();
        Assert.Contains(rows,x=>x.OriginLabel=="User Manager / RADIUS");
        Assert.Contains(rows,x=>x.OriginLabel=="Hotspot");
        Assert.Contains(rows,x=>x.OriginLabel=="غير محدد");
    }
}
