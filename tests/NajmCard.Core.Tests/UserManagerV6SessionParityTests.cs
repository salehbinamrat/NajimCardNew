using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Real v6 session field aliases and account-specific full-read
/// fallback.  All test rows are synthetic, no live RouterOS mutations.</summary>
public sealed class UserManagerV6SessionParityTests
{
    private sealed class Router(bool projectedMiss=false) : IRouterCommandClient
    {
        public int FullReads { get; private set; }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            if(command=="/system/resource/print")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    [new Dictionary<string,string>{{"version","6.49.19"}}]);
            Assert.Equal("/tool/user-manager/session/print",command);
            if(attributes is null)FullReads++;
            IReadOnlyList<IReadOnlyDictionary<string,string>> result=
                attributes is not null && projectedMiss
                    ?[new Dictionary<string,string>{{".id","*10"},
                        {"user","other-card"},
                        {"from-time","nov/13/2026 12:00:00"}}]
                    :[
                        new Dictionary<string,string>
                        {
                            [".id"]="*10",["username"]="3747686346",
                            ["from-time"]="nov/13/2026 12:00:00",
                            ["till-time"]="nov/13/2026 12:45:00",
                            ["user-ip"]="10.0.192.45",
                            ["calling-station-id"]="AA:BB:CC:DD:EE:FF",
                            ["uptime"]="45m",["download"]="1048576",
                            ["upload"]="524288",["status"]="Start & Stop & Interim",
                            ["active"]="no"
                        },
                        new Dictionary<string,string>
                        {
                            [".id"]="*11",["user"]="37476863460",
                            ["user-ip"]="10.0.50.11",["active"]="no"
                        }
                    ];
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Legacy_V6_Aliases_Keep_IP_CallingStation_And_Times()
    {
        var router=new Router();
        var snapshot=await new RouterGateway(router)
            .ReadUmSessionsForAccountAsync("3747686346");
        var row=Assert.Single(snapshot.Rows);
        Assert.Equal("10.0.192.45",row.Address);
        Assert.Equal("AA:BB:CC:DD:EE:FF",row.Mac);
        Assert.Equal("Start & Stop & Interim",row.RawStatus);
        Assert.Equal("nov/13/2026 12:45:00",row.Ended);
        Assert.Equal(1048576,row.DownloadBytes);
        Assert.False(row.IsActive);
        Assert.Equal(0,router.FullReads);
    }

    [Fact]
    public async Task Missing_V6_Projection_Triggers_Full_Read_For_Selected_Account()
    {
        var router=new Router(projectedMiss:true);
        var snapshot=await new RouterGateway(router)
            .ReadUmSessionsForAccountAsync("3747686346","*F");
        var row=Assert.Single(snapshot.Rows);
        Assert.Equal("10.0.192.45",row.Address);
        Assert.Equal(1,router.FullReads);
        Assert.Contains("فحص إضافي",snapshot.Diagnostic);
    }

    [Fact]
    public async Task Similar_Prefixed_Card_Never_Counts_As_Own_Session()
    {
        var router=new Router();
        var snapshot=await new RouterGateway(router)
            .ReadUmSessionsForAccountAsync("37476863460");
        Assert.Single(snapshot.Rows);
        Assert.Equal("37476863460",snapshot.Rows[0].Name);
    }
}
