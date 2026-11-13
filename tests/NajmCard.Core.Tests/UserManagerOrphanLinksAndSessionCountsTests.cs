using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Legacy RouterOS v6 recovery contracts: incomplete relations may
/// not suppress valid profiles, while session counts must be exact.</summary>
public sealed class UserManagerOrphanLinksAndSessionCountsTests
{
    private sealed class LegacyRouter(bool missingRelation=false,
        bool duplicateSession=false) : IRouterCommandClient
    {
        public readonly List<string> Commands=[];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Commands.Add(command);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=command switch
            {
                "/system/resource/print" =>
                    [new Dictionary<string,string>{{"version","6.49.19"}}],
                "/tool/user-manager/profile/print" =>
                    [
                        new Dictionary<string,string>{{".id","*A"},{"name","200RY"},{"price","200"}},
                        new Dictionary<string,string>{{".id","*B"},{"name","6000RY"},{"price","6000"}}
                    ],
                "/tool/user-manager/profile/limitation/print" =>
                    [new Dictionary<string,string>{{".id","*C"},{"name","2GB"},
                        {"transfer-limit","2147483648"}}],
                "/tool/user-manager/profile/profile-limitation/print" =>
                    [
                        new Dictionary<string,string>{{"profile","*A"},{"limitation","*C"}},
                        missingRelation
                            ?new Dictionary<string,string>{{"profile","*missing"}}
                            :new Dictionary<string,string>{{"profile","*removed"},{"limitation","*expired"}}
                    ],
                "/tool/user-manager/session/print" =>
                    duplicateSession
                        ?[
                            new Dictionary<string,string>{{".id","*10"},{"user","12345"}},
                            new Dictionary<string,string>{{".id","*10"},{"user","12345"}}
                        ]
                        :Enumerable.Range(0,163)
                            .Select(i=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>
                            {
                                [".id"]="*"+(i+16).ToString("X"),
                                ["user"]=i%3==0?"other-user":"12345",
                                ["from-time"]="nov/13/2026 15:46:58",
                                ["active"]="no",["download"]="2048",["upload"]="1024"
                            }).ToArray(),
                _=>throw new InvalidOperationException("Unexpected "+command)
            };
            return Task.FromResult(rows);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Orphaned_Or_Incomplete_Legacy_Links_Do_Not_Hide_Real_Profiles(
        bool missingRelation)
    {
        var router=new LegacyRouter(missingRelation);
        var data=await new RouterGateway(router).ReadUmPlansAsync();
        Assert.Equal(RouterOsGeneration.V6,data.Generation);
        Assert.False(data.IsComplete);
        Assert.Contains("Profile",data.Rows.Select(row=>row.Kind));
        Assert.Equal(2,data.Rows.Count(row=>row.Kind=="Profile"));
        Assert.Equal(["200RY","6000RY"],data.Rows
            .Where(x=>x.Kind=="Profile").Select(x=>x.Name).ToArray());
        Assert.Equal("2GB",data.Rows.Single(x=>x.Name=="200RY").AssociatedLimits);
        Assert.Contains("روابط",data.Diagnostic);
        Assert.Equal(1,router.Commands.Count(x=>x==
            "/tool/user-manager/profile/profile-limitation/print"));
    }

    [Fact]
    public async Task Legacy_Sessions_Count_All_Records_Not_Only_Twenty_Visible_Rows()
    {
        var router=new LegacyRouter();
        var sessions=await new RouterGateway(router).ReadUmSessionsAsync();
        Assert.Equal(163,sessions.Rows.Count);
        Assert.Equal(108,sessions.Rows.Count(s=>s.Name=="12345"));
        Assert.All(sessions.Rows,s=>Assert.NotNull(s.StartedAt));
    }

    [Fact]
    public async Task Duplicate_Router_Session_Ids_Cannot_Be_Treated_As_Real_Count()
    {
        await Assert.ThrowsAsync<InvalidDataException>(()=>
            new RouterGateway(new LegacyRouter(duplicateSession:true))
                .ReadUmSessionsAsync());
    }

    [Theory]
    [InlineData("nov/02/2026 18:28:12")]
    [InlineData("Nov/2/2026 18:28:12")]
    [InlineData("11/2/2026 18:28:12")]
    public void Accepts_Real_RouterOs_Date_Formats(string value)
    {
        var parsed=RouterGateway.ParseUmDate(value);
        Assert.NotNull(parsed);
        Assert.Equal(2026,parsed.Value.Year);
        Assert.Equal(11,parsed.Value.Month);
        Assert.Equal(2,parsed.Value.Day);
    }
}
