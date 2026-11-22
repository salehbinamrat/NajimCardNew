using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Delivery.Tests;

public sealed class VoucherPreparationStage4Tests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"najm-preparation",Guid.NewGuid().ToString("N"));
    private sealed class Reader:IRouterCommandClient
    {
        public string Serial="stage4-preparer";
        public bool Denied;
        public string? MissingMenu;
        public Action? AfterScan;
        public readonly List<string> Writes=[];
        public HashSet<string> Occupied=[];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string path,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            if(!path.EndsWith("/print")){Writes.Add(path);throw new InvalidOperationException("No writes allowed");}
            if(path==MissingMenu)throw new RouterOsTrapException("no such command");
            if(path=="/system/routerboard/print")return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                [new Dictionary<string,string>{{"serial-number",Serial}}]);
            if(path=="/ppp/secret/print"&&Denied)throw new RouterOsTrapException("not enough permissions");
            if(path=="/tool/user-manager/user/print")AfterScan?.Invoke();
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(path=="/ppp/secret/print"
                ?Occupied.Select(n=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>{{"name",n}}).ToArray():[]);
        }
    }
    private static VoucherGenerationOptions Options(int count=1)=>new(count,"",4,"01",true,"gold",UsernameOnly:true);

    [Fact]
    public async Task OtherServicesOnRouterAndInLocalStoreAreReservedBeforeSaving()
    {
        using var store=new VoucherStore(_root);var reader=new Reader();var gateway=new RouterGateway(reader);
        var id=await gateway.ReadRouterIdentityAsync();
        reader.Occupied=Enumerable.Range(0,14).Select(i=>Convert.ToString(i,2).PadLeft(4,'0')).ToHashSet();
        store.SaveBatch([new("1110","","gold","old",DateTimeOffset.UtcNow,Service:"userman",Customer:"admin"){RouterId=id}]);
        var cards=await new VoucherBatchPreparer(gateway,store).PrepareAsync(Options(),"hotspot","");
        Assert.Equal("1111",Assert.Single(cards).Username);Assert.Equal(2,store.GetVouchers().Count);Assert.Empty(reader.Writes);
    }
    [Fact]
    public async Task ForbiddenNamespaceLeavesNoLocallyAdoptedBatch()
    {
        using var store=new VoucherStore(_root);var reader=new Reader{Denied=true};
        await Assert.ThrowsAsync<RouterOsTrapException>(()=>new VoucherBatchPreparer(new(reader),store).PrepareAsync(Options(),"hotspot",""));
        Assert.Empty(store.GetVouchers());Assert.Empty(reader.Writes);
    }
    [Theory]
    [InlineData("hotspot","/ip/hotspot/user/print")]
    [InlineData("pppoe","/ppp/secret/print")]
    [InlineData("userman","/tool/user-manager/user/print")]
    public async Task MissingSelectedServiceCannotBeMistakenForAnEmptyNamespace(string service,string path)
    {
        using var store=new VoucherStore(_root);var reader=new Reader{MissingMenu=path};
        await Assert.ThrowsAsync<RouterOsTrapException>(()=>new VoucherBatchPreparer(new(reader),store).PrepareAsync(Options(),service,"admin"));
        Assert.Empty(store.GetVouchers());
    }
    [Fact]
    public async Task SwitchingRouterDuringPreflightDoesNotPersistCards()
    {
        using var store=new VoucherStore(_root);var reader=new Reader();reader.AfterScan=()=>reader.Serial="different-device";
        await Assert.ThrowsAsync<RouterIdentityMismatchException>(()=>new VoucherBatchPreparer(new(reader),store).PrepareAsync(Options(),"hotspot",""));
        Assert.Empty(store.GetVouchers());
    }
    [Fact]
    public void AtomicLocalRecheckRejectsCrossServiceRacesAndKeepsBothRoutersIndependent()
    {
        using var store=new VoucherStore(_root);
        var card=new Voucher("card","","gold","old",DateTimeOffset.UtcNow){RouterId="router-a"};
        store.SavePreparedBatch([card]);
        Assert.Throws<InvalidOperationException>(()=>store.SavePreparedBatch([card with{Service="pppoe",BatchId="new"}]));
        store.SavePreparedBatch([card with{RouterId="router-b"}]);
        Assert.Equal(2,store.GetVouchers().Count);
    }
    [Fact]
    public void UnknownLegacyOwnershipIsReservedConservativelyWithoutReassigningIt()
    {
        using var store=new VoucherStore(_root);
        var card=new Voucher("card","","gold","old",DateTimeOffset.UtcNow);
        store.SaveBatch([card]);
        Assert.Throws<InvalidOperationException>(()=>store.SavePreparedBatch([card with{RouterId="known",Service="pppoe"}]));
        Assert.Equal("",Assert.Single(store.GetVouchers()).RouterId);
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
