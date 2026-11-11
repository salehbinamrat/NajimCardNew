using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerV7RadiusReadinessTests
{
    private sealed class Spy : IRouterCommandClient
    {
        public string Version="7.20";
        public bool RadiusEnabled=true;
        public bool RadiusHotspot=true;
        public bool NasRegistered=true;
        public bool HotspotUsesRadius=true;
        public readonly List<string> Paths=[];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Paths.Add(command);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=command switch
            {
                "/system/resource/print" =>
                    [new Dictionary<string,string>{{"version",Version}}],
                "/user-manager/print" =>
                    [new Dictionary<string,string>{{"enabled","yes"},{"use-profiles","yes"}}],
                "/user-manager/router/print" when NasRegistered =>
                    [new Dictionary<string,string>{{"name","nas"},{"address","127.0.0.1"},{"disabled","no"}}],
                "/user-manager/router/print" => [],
                "/ip/hotspot/print" =>
                    [new Dictionary<string,string>{{"name","hotspot1"},{"profile","hsprof"},{"disabled","no"}}],
                "/ip/hotspot/profile/print" =>
                    [new Dictionary<string,string>{{"name","hsprof"},{"use-radius",HotspotUsesRadius?"yes":"no"}}],
                "/radius/print" =>
                    [new Dictionary<string,string>{{"service",RadiusHotspot?"hotspot":"login"},
                        {"address","127.0.0.1"},{"disabled",RadiusEnabled?"no":"yes"}}],
                _ => throw new InvalidOperationException(command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task ReadyConfiguration_ReportsOnlyConfigurationNotLiveAuthentication()
    {
        var spy=new Spy();
        var report=await new RouterGateway(spy).DiagnoseUserManagerV7RadiusAsync();
        Assert.True(report.ConfigurationReady);
        Assert.False(report.LiveLoginVerified);
        Assert.Contains("RADIUS",report.Message);
        Assert.DoesNotContain(spy.Paths,p=>p.EndsWith("/add")||p.EndsWith("/set"));
    }

    [Theory]
    [InlineData(false,true,true,true)]
    [InlineData(true,false,true,true)]
    [InlineData(true,true,false,true)]
    [InlineData(true,true,true,false)]
    public async Task MisconfiguredNasOrHotspot_IsNotReportedReady(
        bool enabled,bool service,bool nas,bool hotspot)
    {
        var spy=new Spy { RadiusEnabled=enabled,RadiusHotspot=service,
            NasRegistered=nas,HotspotUsesRadius=hotspot };
        var report=await new RouterGateway(spy).DiagnoseUserManagerV7RadiusAsync();
        Assert.False(report.ConfigurationReady);
        Assert.False(report.LiveLoginVerified);
        Assert.NotEmpty(report.Issues);
    }

    [Fact]
    public async Task VersionSix_FailsClosedBeforeV7Reads()
    {
        var spy=new Spy{Version="6.49.19"};
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(spy).DiagnoseUserManagerV7RadiusAsync());
        Assert.Equal(["/system/resource/print"],spy.Paths);
    }

    [Fact]
    public async Task MissingRequiredMenu_DoesNotReportSuccess()
    {
        var spy=new Spy { NasRegistered=false };
        var report=await new RouterGateway(spy).DiagnoseUserManagerV7RadiusAsync();
        Assert.False(report.ConfigurationReady);
    }
}
