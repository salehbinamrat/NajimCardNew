using NajmCard.Core.Models;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class ConnectedUsersProjectionTests
{
    private static UmSessionRow Um(string id,string user,string ip,bool? active)=>
        new(id,user,"nov/13/2026 12:00:00","غير متاح","15m",100,20,
            ip,"AA:BB:CC:DD:EE:FF",active,null,"Start & Interim");

    [Fact]
    public void Hotspot_And_Manager_Tabs_Show_Only_Requested_Source()
    {
        HotspotSession[] hotspot=
        [new("*1","first","10.0.0.2","11:22","10m")];
        UmSessionRow[] manager=
        [Um("*s1","second","10.0.0.3",true),
         Um("*s2","old","10.0.0.4",false),
         Um("*s3","unknown","10.0.0.5",null)];

        var hotspotOnly=ConnectedUsersProjection.ForView("hotspot",hotspot,manager);
        var managerOnly=ConnectedUsersProjection.ForView("userman",hotspot,manager);
        var all=ConnectedUsersProjection.ForView("all",hotspot,manager);

        Assert.Single(hotspotOnly);
        Assert.Equal("الهوتسبوت",hotspotOnly[0].Source);
        Assert.Single(managerOnly);
        Assert.Equal("second",managerOnly[0].User);
        Assert.Equal("اليوزر مانجر",managerOnly[0].Source);
        Assert.Equal(2,all.Count);
        Assert.DoesNotContain(all,x=>x.User=="old"||x.User=="unknown");
    }

    [Fact]
    public void All_DeDuplicates_Only_Same_User_With_Confirmed_Same_IP()
    {
        HotspotSession[] hotspot=
        [new("*h1","same","10.0.0.2","11:22","10m"),
         new("*h2","same","10.0.0.3","11:33","12m")];
        UmSessionRow[] manager=[Um("*u1","same","10.0.0.2",true),
            Um("*u2","same","غير متاح",true)];
        var all=ConnectedUsersProjection.ForView("all",hotspot,manager);

        Assert.Equal(3,all.Count);
        Assert.Single(all,x=>x.Source=="الهوتسبوت + اليوزر مانجر");
        Assert.Contains(all,x=>x.Address=="غير متاح" &&
            x.Source=="اليوزر مانجر");
        Assert.Contains(all,x=>x.Address=="10.0.0.3" &&
            x.Source=="الهوتسبوت");
    }

    [Fact]
    public void Different_Account_With_Same_IP_Does_Not_Get_Merged()
    {
        HotspotSession[] hotspot=[new("*h1","card-a","10.0.0.2","","1m")];
        UmSessionRow[] manager=[Um("*u1","card-b","10.0.0.2",true)];
        Assert.Equal(2,ConnectedUsersProjection.ForView("all",hotspot,manager).Count);
    }

    [Fact]
    public void Unsupported_View_Is_Not_Silently_Treated_As_All()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            ConnectedUsersProjection.ForView("pppoe",[],[]));
    }
}
