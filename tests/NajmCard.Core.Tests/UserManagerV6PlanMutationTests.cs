using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// V6 API simulation; all updates verified against exact IDs. No real
/// MikroTik router is touched. These tests enforce non-destructive failure.
/// </summary>
public sealed class UserManagerV6PlanMutationTests
{
    private sealed class LegacySpy : IRouterCommandClient,IRouterQueryClient,IRouterReceiptClient
    {
        public int Writes;
        public int PlanReads;
        public bool KnownOwner=true;
        public bool Linked;
        public bool WrongOwner;
        public bool LostReply;
        public Dictionary<string,string>? Profile;
        public Dictionary<string,string>? Limitation;
        public readonly List<(string Path,Dictionary<string,string> Args)> Calls=[];

        private static Dictionary<string,string> Copy(IReadOnlyDictionary<string,string> row)=>
            row.ToDictionary(x=>x.Key,x=>x.Value);
        private void Log(string path,IReadOnlyDictionary<string,string>? attrs)=>
            Calls.Add((path,attrs is null?new():Copy(attrs)));

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Log(command,attributes);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
            switch(command)
            {
                case "/system/resource/print":
                    rows=[new Dictionary<string,string>{{"version","6.49.19"}}];break;
                case "/tool/user-manager/customer/print":
                    rows=KnownOwner?[new Dictionary<string,string>{{".id","*E"},{"login","admin"}}]:[];
                    break;
                case "/tool/user-manager/profile/print":
                    PlanReads++;
                    rows=Profile is null?[]:[Copy(Profile)];
                    break;
                case "/tool/user-manager/profile/limitation/print":
                    rows=Limitation is null?[]:[Copy(Limitation)];
                    break;
                case "/tool/user-manager/profile/profile-limitation/print":
                    rows=Linked?
                        [new Dictionary<string,string>{{".id","*B"},{"profile","Monthly"},
                            {"limitation","Quota"}}]:[];
                    break;
                case "/tool/user-manager/user/print":
                    rows=[];break;
                case "/tool/user-manager/profile/set":
                    Writes++;
                    Assert.NotNull(Profile);
                    foreach(var (key,value) in attributes!)
                        if(key!=".id")Profile![key]=value;
                    rows=[];
                    break;
                case "/tool/user-manager/profile/remove":
                    Writes++;
                    Profile=null;rows=[];break;
                default: throw new InvalidOperationException("Unexpected "+command);
            }
            if(LostReply&&Writes>0)throw new IOException("Connection dropped after write");
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Log(command+" ?"+id,attributes);
            IReadOnlyList<IReadOnlyDictionary<string,string>> result=command switch
            {
                "/tool/user-manager/profile/print" when
                    Profile is not null && Profile[".id"]==id =>
                    [Copy(Profile)],
                "/tool/user-manager/profile/limitation/print" when
                    Limitation is not null && Limitation[".id"]==id =>
                    [Copy(Limitation)],
                "/tool/user-manager/profile/profile-limitation/print" when
                    Linked && id=="*B" =>
                    [new Dictionary<string,string>{{".id","*B"},{"profile","Monthly"},
                        {"limitation","Quota"}}],
                _=>[]
            };
            if(WrongOwner&&result.Count>0 && result[0].ContainsKey("owner"))
            {
                var changed=Copy(result[0]);
                changed["owner"]="other";
                result=[changed];
            }
            return Task.FromResult(result);
        }

        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Log(command,attributes);
            Writes++;
            if(command=="/tool/user-manager/profile/add")
            {
                Profile=Copy(attributes!);
                Profile[".id"]="*A";
            }
            else if(command=="/tool/user-manager/profile/profile-limitation/add")
            {
                Linked=true;
                Assert.Equal("Monthly",attributes!["profile"]);
                Assert.Equal("Quota",attributes["limitation"]);
            }
            else throw new InvalidOperationException("Unexpected "+command);
            if(LostReply)throw new IOException("Result lost");
            return Task.FromResult(new RouterCommandReply([],
                new Dictionary<string,string>{{"ret",command.Contains("profile-limitation")?"*B":"*A"}}));
        }

        public void SeedProfile(string owner="admin") =>
            Profile=new Dictionary<string,string> {
                [".id"]="*A",["name"]="Monthly",["owner"]=owner,
                ["price"]="250",["validity"]="30d",["starts-at"]="now"
            };

        public void SeedLimit() =>
            Limitation=new Dictionary<string,string> {
                [".id"]="*C",["name"]="Quota",["owner"]="admin",
                ["transfer-limit"]="1073741824",["download-limit"]="0",
                ["upload-limit"]="0",["uptime-limit"]="30d",
                ["rate-limit-rx"]="256k",["rate-limit-tx"]="1024k"
            };
    }

    private static UmPlanDraft Profile(string owner="admin",string starts="first-auth")=>
        new("Profile","Monthly","250","30d",starts,"","","","","")
        {Owner=owner};

    [Fact]
    public async Task V6AddUsesLegacyStartsAtAndVerifiesOwnerAndReceipt()
    {
        var spy=new LegacySpy();
        var result=await new RouterGateway(spy).ChangeUmPlanAsync("add",Profile());
        Assert.True(result.Success,result.Message);
        Assert.False(result.Uncertain);
        Assert.Equal(1,spy.Writes);
        Assert.Contains(spy.Calls,x=>x.Path=="/tool/user-manager/profile/add"
            && x.Args["owner"]=="admin" && x.Args["starts-at"]=="logon");
        Assert.Equal("admin",spy.Profile!["owner"]);
    }

    [Fact]
    public async Task WrongOrUnknownOwnerDoesNotSendMutation()
    {
        var spy=new LegacySpy{KnownOwner=false};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).ChangeUmPlanAsync("add",Profile()));
        Assert.Equal(0,spy.Writes);
        var unscoped=new LegacySpy();
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(unscoped).ChangeUmPlanAsync("add",Profile(owner:"")));
        Assert.Equal(0,unscoped.Writes);
    }

    [Fact]
    public async Task ExactIdMismatchStopsLegacySetBeforeWrite()
    {
        var spy=new LegacySpy{WrongOwner=true};
        spy.SeedProfile();
        var selected=new UmPlanRow("*A","Profile","Monthly","250","30d",
            "now","","","","","","",""){Owner="admin"};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).ChangeUmPlanAsync("set",Profile(starts:"assigned"),selected));
        Assert.Equal(0,spy.Writes);
    }

    [Fact]
    public async Task LinkedProfileCannotBeRemoved()
    {
        var spy=new LegacySpy{Linked=true};
        spy.SeedProfile();spy.SeedLimit();
        var selected=new UmPlanRow("*A","Profile","Monthly","250","30d",
            "now","","","","","","",""){Owner="admin"};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).ChangeUmPlanAsync("remove",Profile(starts:"assigned"),selected));
        Assert.Equal(0,spy.Writes);
    }

    [Fact]
    public async Task LegacyLinkIsOneWriteWithExactIdVerificationAndNoDuplicate()
    {
        var spy=new LegacySpy();
        spy.SeedProfile();spy.SeedLimit();
        var gateway=new RouterGateway(spy);
        var added=await gateway.LinkUmProfileLimitationV6Async("Monthly","Quota","admin");
        Assert.True(added.Success,added.Message);
        Assert.Equal(1,spy.Writes);
        var repeated=await gateway.LinkUmProfileLimitationV6Async("Monthly","Quota","admin");
        Assert.True(repeated.Success);
        Assert.Equal(1,spy.Writes);
    }

    [Fact]
    public async Task LostAcknowledgementIsUncertainAndNeverRetried()
    {
        var spy=new LegacySpy{LostReply=true};
        var result=await new RouterGateway(spy).ChangeUmPlanAsync("add",Profile());
        Assert.False(result.Success);
        Assert.True(result.Uncertain);
        Assert.Equal(1,spy.Writes);
    }

    [Fact]
    public async Task LegacySetInvalidatesCachedDefinitionsBeforeNextRead()
    {
        var spy=new LegacySpy();
        spy.SeedProfile();spy.SeedLimit();
        var gateway=new RouterGateway(spy);
        var original=await gateway.ReadCachedUmPlansAsync();
        var chosen=original.Rows.Single(x=>x.Kind=="Profile");
        var result=await gateway.ChangeUmPlanAsync("set",Profile(),chosen);
        Assert.True(result.Success,result.Message);
        var refreshed=await gateway.ReadCachedUmPlansAsync();
        Assert.NotSame(original,refreshed);
        Assert.Equal("logon",refreshed.Rows.Single(x=>x.Kind=="Profile").StartsWhen);
    }

    [Fact]
    public async Task PlanCacheReusesCompleteReadAndForceRefreshBypassesIt()
    {
        var spy=new LegacySpy();
        spy.SeedProfile();spy.SeedLimit();
        var gateway=new RouterGateway(spy);
        var first=await gateway.ReadCachedUmPlansAsync();
        var again=await gateway.ReadCachedUmPlansAsync();
        Assert.Same(first,again);
        Assert.Equal(1,spy.PlanReads);
        await gateway.ReadCachedUmPlansAsync(force:true);
        Assert.Equal(2,spy.PlanReads);
        gateway.InvalidateUmPlansCache();
        await gateway.ReadCachedUmPlansAsync();
        Assert.Equal(3,spy.PlanReads);
    }
}
