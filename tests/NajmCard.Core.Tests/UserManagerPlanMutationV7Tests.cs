using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerPlanMutationV7Tests
{
    private sealed class Spy(string version="7.21") : IRouterCommandClient
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
                    [new Dictionary<string,string>{{"version",version}}],
                "/user-manager/profile/print" => [],
                "/user-manager/limitation/print" => [],
                _=>throw new InvalidOperationException("Unexpected "+command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task LegacyV6MutationsAreDeniedBeforeAnyWrite()
    {
        var spy=new Spy("6.49.19");
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(spy).ChangeUmPlanAsync("add",
                new UmPlanDraft("Profile","Fast","250","30d","","","","","","")));
        Assert.DoesNotContain(spy.Commands,x=>x.EndsWith("/add")||
            x.EndsWith("/set")||x.EndsWith("/remove"));
    }

    [Fact]
    public async Task BlankNameOrInvalidActionCannotChangeRouter()
    {
        var spy=new Spy();
        await Assert.ThrowsAsync<ArgumentException>(()=>
            new RouterGateway(spy).ChangeUmPlanAsync("add",
                new UmPlanDraft("Profile"," ","100","30d","","","","","","")));
        await Assert.ThrowsAsync<ArgumentException>(()=>
            new RouterGateway(spy).ChangeUmPlanAsync("execute",
                new UmPlanDraft("Profile","Fast","100","30d","","","","","","")));
        Assert.DoesNotContain(spy.Commands,x=>x.EndsWith("/add")||
            x.EndsWith("/set")||x.EndsWith("/remove"));
    }

    [Fact]
    public async Task NoVerificationTransportMeansNoMutation()
    {
        var spy=new Spy();
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(spy).ChangeUmPlanAsync("add",
                new UmPlanDraft("Profile","Fast","100","30d","","","","","","")));
        Assert.DoesNotContain(spy.Commands,x=>x.EndsWith("/add")||
            x.EndsWith("/set")||x.EndsWith("/remove"));
    }
}
