using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>RouterOS v7 provisioning is a verified two-step transaction, not a print operation.</summary>
public sealed class UserManagerV7ProvisioningTests
{
    private sealed class Fake : IRouterCommandClient, IRouterReceiptClient,
        IRouterQueryClient, IRouterNameQueryClient
    {
        public string Version="7.21.1";
        public bool NameExists;
        public bool HotspotCollision;
        public bool ProfilesEnabled=true;
        public bool ProfileExists=true;
        public bool DropFirstReply;
        public bool RejectAssignment;
        public bool ReturnFirstId=true;
        public bool ReturnSecondId=true;
        public string SentPassword="<unset>";
        public readonly List<string> Commands=[];
        public readonly List<string> Lookups=[];
        public int Writes;
        private bool created;
        private bool assigned;

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Commands.Add(command);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=command switch
            {
                "/system/resource/print" =>
                    [new Dictionary<string,string>{{"version",Version}}],
                "/user-manager/print" =>
                    [new Dictionary<string,string>
                        {{"enabled","yes"},{"use-profiles",ProfilesEnabled?"yes":"no"}}],
                "/user-manager/profile/print" when ProfileExists =>
                    [new Dictionary<string,string>{{"name","gold"}}],
                "/user-manager/profile/print" => [],
                _ => throw new InvalidOperationException(command)
            };
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByNameAsync(
            string command,string name,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Lookups.Add(command);
            Assert.Equal("246800",name);
            Assert.DoesNotContain("password",attributes![".proplist"],StringComparison.OrdinalIgnoreCase);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=
                ((command=="/user-manager/user/print"&&NameExists) ||
                 (command=="/ip/hotspot/user/print"&&HotspotCollision))
                ?[new Dictionary<string,string>{{".id","*E"},{"name",name}}]:[];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            if(command=="/user-manager/user/print" && created && id=="*C")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    [new Dictionary<string,string>{{".id","*C"},{"name","246800"},{"disabled","no"}}]);
            if(command=="/user-manager/user-profile/print" && assigned && id=="*D")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    [new Dictionary<string,string>{{".id","*D"},{"user","246800"},{"profile","gold"}}]);
            throw new InvalidOperationException("Unverified path or ID: "+command+"/"+id);
        }

        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Commands.Add(command);Writes++;
            if(command=="/user-manager/user/add")
            {
                Assert.Equal("246800",attributes!["name"]);
                SentPassword=attributes["password"];
                if(DropFirstReply)throw new IOException("lost reply after add");
                created=true;
                return Task.FromResult(new RouterCommandReply([],
                    ReturnFirstId
                        ?new Dictionary<string,string>{{"ret","*C"}}
                        :new Dictionary<string,string>()));
            }
            if(command=="/user-manager/user-profile/add")
            {
                Assert.Equal("246800",attributes!["user"]);
                Assert.Equal("gold",attributes["profile"]);
                if(RejectAssignment)throw new RouterOsTrapException("not allowed");
                assigned=true;
                return Task.FromResult(new RouterCommandReply([],
                    ReturnSecondId
                        ?new Dictionary<string,string>{{"ret","*D"}}
                        :new Dictionary<string,string>()));
            }
            throw new InvalidOperationException(command);
        }
    }

    [Fact]
    public async Task UsernameOnlyAccount_UsesExplicitBlankPasswordAndVerifiesProfile()
    {
        var fake=new Fake();
        var result=await new RouterGateway(fake).CreateUserManagerV7AccountAsync("246800","","gold");
        Assert.True(result.Success,result.Message);
        Assert.True(result.ProfileAssigned);
        Assert.False(result.Uncertain);
        Assert.Equal("246800",result.Username);
        Assert.Equal("",fake.SentPassword);
        Assert.Equal(2,fake.Writes);
        Assert.Equal(3,fake.Lookups.Count);
        Assert.Contains("/user-manager/user/print",fake.Lookups);
        Assert.Contains("/ip/hotspot/user/print",fake.Lookups);
        Assert.Contains("/ppp/secret/print",fake.Lookups);
    }

    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task DuplicateAcrossServices_BlocksAllWrites(bool umCollision,bool hotspotCollision)
    {
        var fake=new Fake{NameExists=umCollision,HotspotCollision=hotspotCollision};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(fake).CreateUserManagerV7AccountAsync("246800","","gold"));
        Assert.Equal(0,fake.Writes);
    }

    [Fact]
    public async Task OldVersion_RejectsV7ProvisionBeforeAnyWrite()
    {
        var fake=new Fake{Version="6.49.19"};
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(fake).CreateUserManagerV7AccountAsync("246800","","gold"));
        Assert.Equal(0,fake.Writes);
    }

    [Fact]
    public async Task ProfileModeOrMissingProfile_BlocksAllWrites()
    {
        var fake=new Fake{ProfilesEnabled=false};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(fake).CreateUserManagerV7AccountAsync("246800","","gold"));
        Assert.Equal(0,fake.Writes);
        fake.ProfilesEnabled=true;
        fake.ProfileExists=false;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(fake).CreateUserManagerV7AccountAsync("246800","","gold"));
        Assert.Equal(0,fake.Writes);
    }

    [Fact]
    public async Task UnknownAccountCreationOutcome_IsNotRetriedOrAssigned()
    {
        var fake=new Fake{DropFirstReply=true};
        var result=await new RouterGateway(fake).CreateUserManagerV7AccountAsync("246800","","gold");
        Assert.False(result.Success);
        Assert.True(result.Uncertain);
        Assert.Equal(1,fake.Writes);
        Assert.False(result.ProfileAssigned);
    }

    [Fact]
    public async Task AccountCreatedButProfileRejected_IsNotReportedReady()
    {
        var fake=new Fake{RejectAssignment=true};
        var result=await new RouterGateway(fake).CreateUserManagerV7AccountAsync("246800","","gold");
        Assert.False(result.Success);
        Assert.True(result.AccountCreated);
        Assert.False(result.ProfileAssigned);
        Assert.Equal(2,fake.Writes);
    }

    [Fact]
    public async Task MissingCreationReceipt_IsUncertainAndCannotAssign()
    {
        var fake=new Fake{ReturnFirstId=false};
        var result=await new RouterGateway(fake).CreateUserManagerV7AccountAsync("246800","","gold");
        Assert.False(result.Success);
        Assert.True(result.Uncertain);
        Assert.Equal(1,fake.Writes);
    }
}
