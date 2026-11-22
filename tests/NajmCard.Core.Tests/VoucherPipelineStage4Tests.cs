using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class VoucherPipelineStage4Tests
{
    [Fact]
    public void ARemainingNumberIsAlwaysGeneratedInAnAlmostFullSpace()
    {
        var reserved=Enumerable.Range(0,65535).Select(n=>Convert.ToString(n,2).PadLeft(16,'0'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Four independent trials make the old 200 random probes incapable
        // of reliably finding the one free number; the new algorithm must guarantee it.
        for(int i=0;i<4;i++)
            Assert.Equal("1111111111111111",Assert.Single(VoucherGenerator.Generate(
                new(1,"",16,"01",true,"gold"),reserved)).Username);
    }

    [Fact]
    public void EveryFreeNumberCanBeSelectedWithoutReplacement()
    {
        var reserved=Enumerable.Range(0,9997).Select(n=>"NC-"+n.ToString("D4"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cards=VoucherGenerator.Generate(new(3,"NC-",4,"0123456789",true,"gold"),reserved);
        Assert.Equal(new[]{"NC-9997","NC-9998","NC-9999"},cards.Select(x=>x.Username).Order().ToArray());
    }

    [Fact]
    public void ReservedUnicodeCaseVariantsUseTheSameOrdinalComparerAsLocalUniqueness()
    {
        var reserved=new HashSet<string>(["ſ000"],StringComparer.OrdinalIgnoreCase);
        var expected=Enumerable.Range(0,16).Select(i=>Convert.ToString(i,2).PadLeft(4,'0').Replace('1','s'))
            .Where(n=>!reserved.Contains(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cards=VoucherGenerator.Generate(new(expected.Count,"",4,"s0",true,"gold"),reserved);
        Assert.True(expected.SetEquals(cards.Select(c=>c.Username)));
    }

    private sealed class Router : IRouterCommandClient,IRouterReceiptClient,IRouterQueryClient
    {
        internal int IdentityReads,Adds,Reads;
        internal bool MissingRecord,MissingOwner,BlankName;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string path,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=[];
            if(path=="/system/routerboard/print")
            {IdentityReads++;rows=[new Dictionary<string,string>{{"serial-number","stage4-device"}}];}
            if(BlankName&&path=="/ppp/secret/print")rows=[new Dictionary<string,string>{{"name",""}}];
            return Task.FromResult(rows);
        }
        public Task<RouterCommandReply> CommandWithReceiptAsync(string path,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {Adds++;return Task.FromResult(new RouterCommandReply([],new Dictionary<string,string>{{"ret","*1"}}));}
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(string path,string id,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            Reads++;
            var row=new Dictionary<string,string>{{".id",id},{"name","card"},{"username","card"},
                {"profile","gold"},{"actual-profile","gold"},{"service","pppoe"},{"disabled","false"},{"incomplete","false"}};
            if(!MissingOwner)row["customer"]="admin";
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(MissingRecord?[]:[row]);
        }
    }

    [Fact]
    public async Task AnAddReceiptAloneDoesNotConfirmAPppAccountAndItsProfile()
    {
        var router=new Router{MissingRecord=true};
        var result=await new RouterGateway(router).CreatePppoeVoucherAsync(
            new("card","","gold","batch",DateTimeOffset.UtcNow,Service:"pppoe"));
        Assert.False(result.Success);Assert.True(result.UncertainOutcome);
        Assert.Equal(1,router.Adds);Assert.True(router.Reads>0);
    }

    [Fact]
    public async Task MissingOwnerCannotConfirmUserManagerDelivery()
    {
        var router=new Router{MissingOwner=true};
        var result=await new RouterGateway(router).CreateAndActivateUserManagerV6Async(
            new("card","","gold","batch",DateTimeOffset.UtcNow,Service:"userman",Customer:"admin"),"admin");
        Assert.False(result.Success);Assert.True(result.UncertainOutcome);Assert.Equal(1,router.Adds);
    }

    [Fact]
    public async Task RepeatedIdentityChecksReuseOnlyTheSameAuthenticatedConnection()
    {
        var router=new Router();
        await using var session=new ResilientRouterSession(router,_=>throw new InvalidOperationException());
        var gateway=new RouterGateway(session);var id=await gateway.ReadRouterIdentityAsync();
        for(int i=0;i<100;i++)await gateway.VerifyRouterIdentityAsync(id);
        Assert.Equal(1,router.IdentityReads);
    }

    [Fact]
    public async Task AnEmptyNameInANonemptyInventoryDoesNotProveNoCollision()
    {
        await Assert.ThrowsAsync<InvalidDataException>(()=>new RouterGateway(new Router{BlankName=true})
            .ReadExistingVoucherNamesAsync());
    }
}
