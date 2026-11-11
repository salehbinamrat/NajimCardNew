using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerSafeOperationsTests
{
    private sealed class RouterSpy(string version) :
        IRouterCommandClient, IRouterQueryClient, IRouterReceiptClient
    {
        public readonly List<string> Commands=[];
        public readonly List<string> Queries=[];
        public string AccountName="card123";
        public string AccountId="*A";
        public bool Disabled;
        public bool ChangeIdentityBeforeRead;
        public bool DropWriteReply;
        public bool ProfileEnabled=true;
        public bool ProfileAvailable=true;
        public bool ReturnReceiptId=true;
        public int Writes;
        private bool assigned;

        private IReadOnlyDictionary<string,string> Account()=>
            new Dictionary<string,string>
            {
                [".id"]=AccountId,
                [version.StartsWith('6')?"username":"name"]=
                    ChangeIdentityBeforeRead?"different":AccountName,
                ["disabled"]=Disabled?"yes":"no",
                ["actual-profile"]="gold"
            };

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Commands.Add(command);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
            if(command=="/system/resource/print")
                rows=[new Dictionary<string,string>{{"version",version}}];
            else if(command=="/user-manager/print")
                rows=[new Dictionary<string,string>
                    {{"enabled","yes"},{"use-profiles",ProfileEnabled?"yes":"no"}}];
            else if(command=="/user-manager/profile/print")
                rows=ProfileAvailable?
                    [new Dictionary<string,string>{{".id","*2"},{"name","gold"}}]:[];
            else if(command is "/user-manager/user/set" or "/tool/user-manager/user/set")
            {
                Writes++;
                Disabled=attributes!["disabled"]=="yes";
                if(DropWriteReply)throw new IOException("TCP connection lost after write");
                rows=[];
            }
            else
                throw new InvalidOperationException("Unexpected path "+command);
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Queries.Add(command+" "+id);
            if(command is "/user-manager/user/print" or "/tool/user-manager/user/print")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    id==AccountId?[Account()]:[]);
            if(command=="/user-manager/user-profile/print")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    assigned && id=="*B"?
                    [new Dictionary<string,string>
                        {{".id","*B"},{"user",AccountName},{"profile","gold"},{"state","running"}}]:[]);
            throw new InvalidOperationException("Unexpected exact-ID read "+command);
        }

        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Commands.Add(command);
            if(command!="/user-manager/user-profile/add")
                throw new InvalidOperationException("Unexpected write "+command);
            Writes++;
            if(DropWriteReply)throw new IOException("TCP disconnected after add");
            Assert.Equal(AccountName,attributes!["user"]);
            Assert.Equal("gold",attributes["profile"]);
            assigned=true;
            IReadOnlyDictionary<string,string> receipt=ReturnReceiptId?
                new Dictionary<string,string>{{"ret","*B"}}:new Dictionary<string,string>();
            return Task.FromResult(new RouterCommandReply([],receipt));
        }
    }

    private static UserManagerAccountSelection Selected(RouterOsGeneration generation,
        bool disabled=false,string name="card123") =>
        new(generation,"*A",name,disabled);

    [Theory]
    [InlineData("6.49.19",RouterOsGeneration.V6,"/tool/user-manager/user/set")]
    [InlineData("7.21",RouterOsGeneration.V7,"/user-manager/user/set")]
    public async Task StateChange_IsExactIdReadBeforeAndAfterSingleWrite(
        string version,RouterOsGeneration generation,string expectedPath)
    {
        var spy=new RouterSpy(version);
        var result=await new RouterGateway(spy).SetUserManagerAccountDisabledAsync(
            Selected(generation),true);
        Assert.True(result.Success,result.Message);
        Assert.False(result.Uncertain);
        Assert.Equal(1,spy.Writes);
        Assert.Contains(expectedPath,spy.Commands);
        Assert.Equal(2,spy.Queries.Count);
        Assert.All(spy.Queries,query=>Assert.Contains(" *A",query));
    }

    [Fact]
    public async Task StateMismatchOrRouterGenerationMismatch_FailsBeforeWrite()
    {
        var spy=new RouterSpy("7.21"){ChangeIdentityBeforeRead=true};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).SetUserManagerAccountDisabledAsync(
                Selected(RouterOsGeneration.V7),true));
        spy.ChangeIdentityBeforeRead=false;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).SetUserManagerAccountDisabledAsync(
                Selected(RouterOsGeneration.V6),true));
        Assert.Equal(0,spy.Writes);
    }

    [Fact]
    public async Task FailedWriteResponse_IsUncertainAndNeverRetried()
    {
        var spy=new RouterSpy("7.21"){DropWriteReply=true};
        var result=await new RouterGateway(spy).SetUserManagerAccountDisabledAsync(
            Selected(RouterOsGeneration.V7),true);
        Assert.False(result.Success);
        Assert.True(result.Uncertain);
        Assert.Equal(1,spy.Writes);
    }

    [Fact]
    public async Task DuplicateState_IsVerifiedButNotResent()
    {
        var spy=new RouterSpy("7.21"){Disabled=true};
        var result=await new RouterGateway(spy).SetUserManagerAccountDisabledAsync(
            Selected(RouterOsGeneration.V7,disabled:true),true);
        Assert.True(result.Success);
        Assert.Equal(0,spy.Writes);
    }

    [Fact]
    public async Task RouterOs7_AssignsProfileUsingUserProfileTableAndReadback()
    {
        var spy=new RouterSpy("7.21");
        var result=await new RouterGateway(spy).AssignUserManagerV7ProfileAsync(
            Selected(RouterOsGeneration.V7),"gold");
        Assert.True(result.Success,result.Message);
        Assert.Equal(1,spy.Writes);
        Assert.Contains("/user-manager/user-profile/add",spy.Commands);
        Assert.Contains("/user-manager/user-profile/print *B",spy.Queries);
    }

    [Fact]
    public async Task ProfileAssignmentRefusesDisabledProfilesModeAndMissingProfile()
    {
        var spy=new RouterSpy("7.21"){ProfileEnabled=false};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).AssignUserManagerV7ProfileAsync(
                Selected(RouterOsGeneration.V7),"gold"));
        Assert.Equal(0,spy.Writes);
        spy.ProfileEnabled=true;
        spy.ProfileAvailable=false;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).AssignUserManagerV7ProfileAsync(
                Selected(RouterOsGeneration.V7),"gold"));
        Assert.Equal(0,spy.Writes);
    }

    [Fact]
    public async Task AssignmentDoesNotBlindlyRetryAnUnknownResultOrMissingReceipt()
    {
        var spy=new RouterSpy("7.21"){DropWriteReply=true};
        var result=await new RouterGateway(spy).AssignUserManagerV7ProfileAsync(
            Selected(RouterOsGeneration.V7),"gold");
        Assert.False(result.Success);
        Assert.True(result.Uncertain);
        Assert.Equal(1,spy.Writes);

        var second=new RouterSpy("7.21"){ReturnReceiptId=false};
        var unknown=await new RouterGateway(second).AssignUserManagerV7ProfileAsync(
            Selected(RouterOsGeneration.V7),"gold");
        Assert.False(unknown.Success);
        Assert.True(unknown.Uncertain);
        Assert.Equal(1,second.Writes);
    }

    [Fact]
    public async Task NoV7AssignmentCommandsAreSentToV6()
    {
        var spy=new RouterSpy("6.49.19");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).AssignUserManagerV7ProfileAsync(
                Selected(RouterOsGeneration.V6),"gold"));
        Assert.Equal(0,spy.Writes);
    }
}
