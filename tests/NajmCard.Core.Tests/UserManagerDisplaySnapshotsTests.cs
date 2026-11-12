using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerDisplaySnapshotsTests
{
    private sealed class Router(string version) : IRouterCommandClient
    {
        public readonly List<(string Path,IReadOnlyDictionary<string,string>? Properties)> Calls=[];
        public bool FailSession;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add((command,attributes));
            if(FailSession && command.EndsWith("/session/print",StringComparison.Ordinal))
                throw new IOException("session read failed");
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=command switch
            {
                "/system/resource/print" =>
                    [new Dictionary<string,string>{{"version",version}}],
                "/user-manager/user/print" =>
                    [new Dictionary<string,string>{{".id","*1"},{"name","test-u"},
                        {"disabled","no"},{"shared-users","1"}}],
                "/user-manager/user-profile/print" =>
                    [new Dictionary<string,string>{{".id","*2"},{"user","test-u"},
                        {"profile","Night"},{"state","running active"},{"end-time","2026-11-30 22:00:00"}}],
                "/user-manager/profile/print" =>
                    [new Dictionary<string,string>{{".id","*3"},{"name","Night"},
                        {"price","125"},{"validity","1d"},{"starts-when","first-auth"}}],
                "/user-manager/limitation/print" =>
                    [new Dictionary<string,string>{{".id","*4"},{"name","quota"},
                        {"transfer-limit","1073741824"},{"rate-limit-rx","1024k"},
                        {"rate-limit-tx","256k"}}],
                "/user-manager/profile-limitation/print" =>
                    [new Dictionary<string,string>{{"profile","Night"},{"limitation","quota"}}],
                "/user-manager/session/print" =>
                    [new Dictionary<string,string>{{".id","*5"},{"user","test-u"},
                        {"started","2026-11-11 18:25:00"},{"uptime","2h15m"},
                        {"active","yes"},{"download","1024"},{"upload","512"},
                        {"user-address","10.0.1.20"},{"calling-station-id","10:20:30:40:50:60"}}],
                _=>throw new RouterOsTrapException("Unknown path "+command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task V7_UsersJoinProfilesAndNeverRequestPasswords()
    {
        var router=new Router("7.21");
        var data=await new RouterGateway(router).ReadUmAccountsAsync();
        Assert.Equal(RouterOsGeneration.V7,data.Generation);
        var user=Assert.Single(data.Rows);
        Assert.Equal("test-u",user.Name);
        Assert.Equal("Night",user.Profile);
        // RouterOS v7 distinguishes an enabled account from its currently
        // running user-profile. The dashboard must expose this precise state.
        Assert.Equal(false,user.Disabled);
        Assert.Equal("باقة نشطة",user.Status);
        Assert.Equal("2026-11-30 22:00:00",user.EndTime);
        Assert.All(router.Calls.Where(x=>x.Properties is not null),call=>
            Assert.DoesNotContain("password",call.Properties![".proplist"],
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(router.Calls,x=>x.Path.Contains("/monitor"));
    }

    [Fact]
    public async Task V7_PackagesUseRealLinkedLimitationsNotInventedSpeeds()
    {
        var router=new Router("7.21");
        var rows=await new RouterGateway(router).ReadUmPlansAsync();
        Assert.Equal(2,rows.Rows.Count);
        var profile=Assert.Single(rows.Rows.Where(x=>x.Kind=="Profile"));
        Assert.Equal("Night",profile.Name);
        Assert.Equal("125",profile.Price);
        Assert.Equal("quota",profile.AssociatedLimits);
        var limit=Assert.Single(rows.Rows.Where(x=>x.Kind=="Limitation"));
        Assert.Equal("1073741824",limit.TransferLimit);
        Assert.Equal("1024k",limit.DownloadRate);
    }

    [Fact]
    public async Task V7_SessionsReflectRealDatesIpMacAndTraffic()
    {
        var router=new Router("7.21");
        var data=await new RouterGateway(router).ReadUmSessionsAsync();
        var row=Assert.Single(data.Rows);
        Assert.Equal(true,row.IsActive);
        Assert.Equal(1024,row.DownloadBytes);
        Assert.Equal(512,row.UploadBytes);
        Assert.Equal("10.0.1.20",row.Address);
        Assert.Equal("10:20:30:40:50:60",row.Mac);
        Assert.NotNull(row.StartedAt);
    }

    [Fact]
    public async Task FailureNeverBecomesAnEmptySuccessfulSnapshot()
    {
        var router=new Router("7.21") {FailSession=true};
        await Assert.ThrowsAsync<IOException>(()=>
            new RouterGateway(router).ReadUmSessionsAsync());
    }

    [Fact]
    public async Task UnsupportedRouterOsFailsClosed()
    {
        var router=new Router("8.0");
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(router).ReadUmPlansAsync());
        Assert.Equal(["/system/resource/print"],router.Calls.Select(x=>x.Path));
    }
}
