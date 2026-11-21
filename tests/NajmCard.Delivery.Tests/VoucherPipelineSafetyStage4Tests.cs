using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using NajmCard.TestFixtures;
using Xunit;

namespace NajmCard.Delivery.Tests;

public sealed class VoucherPipelineSafetyStage4Tests:IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"najm-pipeline-safety",Guid.NewGuid().ToString("N"));
    private sealed class Capture(Action<VoucherPipelineProgress> callback):IProgress<VoucherPipelineProgress>
    {public void Report(VoucherPipelineProgress progress)=>callback(progress);}
    private static Voucher Card(string id,string name="card1",string service="userman")=>
        new(name,"","gold","batch",DateTimeOffset.UtcNow,Service:service,Customer:service=="userman"?"admin":""){RouterId=id};

    [Fact]
    public async Task IssuancePreflightSelectsTheCorrectOwnerWhenProfileNamesRepeat()
    {
        var router=new VoucherPipelineRouter{ProfileOwners=["other","admin"]};
        using var operation=new VoucherPipelineOperation(pacer:new VoucherCommandPacer(router.Clock));
        await new RouterGateway(router).VerifyUserManagerV6HotspotReadinessAsync("gold","admin");
        Assert.Equal(0,router.Adds);
    }
    [Fact]
    public async Task IssuancePreflightCannotAssumeAnUnknownProfileOwner()
    {
        var router=new VoucherPipelineRouter{ProfileOwners=[""]};
        using var operation=new VoucherPipelineOperation(pacer:new VoucherCommandPacer(router.Clock));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new RouterGateway(router).VerifyUserManagerV6HotspotReadinessAsync("gold","admin"));
        Assert.Equal(0,router.Adds);
    }

    [Fact]
    public async Task ProgressConfirmsOnlyCommittedCardsAndUnknownStopsTheRemainingBatch()
    {
        using var store=new VoucherStore(_root);var router=new VoucherPipelineRouter{LoseActivationAt=2};
        using var operation=new VoucherPipelineOperation(pacer:new VoucherCommandPacer(router.Clock));
        await using var session=new ResilientRouterSession(router,_=>Task.FromResult<IRouterCommandClient>(router));
        var gateway=new RouterGateway(session);var id=await gateway.ReadRouterIdentityAsync();
        var cards=new[]{Card(id),Card(id,"card2"),Card(id,"card3")};store.SaveBatch(cards);
        operation.Metrics.Progress=new Capture(p=>Assert.Equal(p.Confirmed,store.GetVouchers().Count(c=>c.DeliveryStatus=="SENT")));
        var result=await new VoucherBatchDispatcher(gateway,store).SendAsync(cards);
        Assert.Equal(1,result.Succeeded);Assert.True(result.UncertainOutcome);
        Assert.Equal(2,router.Adds);Assert.Equal(2,router.Activations);
        using var reopened=new VoucherStore(_root);
        Assert.Equal(new[]{"SENT","UNKNOWN","CREATED"},reopened.GetVouchers().OrderBy(c=>c.Username).Select(c=>c.DeliveryStatus));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new VoucherBatchDispatcher(gateway,reopened).SendAsync(cards));
        var reviewed=await new VoucherBatchRecovery(gateway,reopened).ReviewAsync(id,"userman","batch");
        Assert.Equal("SENT",reviewed.Single(c=>c.Username=="card2").DeliveryStatus);
        Assert.Equal(2,router.Adds);Assert.Equal(2,router.Activations);
        Assert.Equal(1,result.Performance!.Unknown);
    }
    [Fact]
    public async Task FailedReceiptCommitLeavesSendingAndDoesNotAdvanceConfirmedProgress()
    {
        using var store=new VoucherStore(_root);var router=new VoucherPipelineRouter();
        using var operation=new VoucherPipelineOperation(pacer:new VoucherCommandPacer(router.Clock));
        var gateway=new RouterGateway(router);var card=Card(await gateway.ReadRouterIdentityAsync(),service:"hotspot");store.SaveBatch([card]);
        using(var db=new SqliteConnection($"Data Source={Path.Combine(_root,"najmcard.db")};Pooling=False"))
        {
            db.Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TRIGGER reject_receipt BEFORE UPDATE ON vouchers WHEN NEW.delivery_status='SENT' BEGIN SELECT RAISE(ABORT,'test disk failure'); END;";cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<SqliteException>(()=>new VoucherBatchDispatcher(gateway,store).SendAsync([card]));
        Assert.Equal(0,operation.Metrics.Snapshot().Confirmed);
        Assert.Equal("SENDING",Assert.Single(store.GetVouchers()).DeliveryStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new VoucherBatchDispatcher(gateway,store).SendAsync([card]));
        Assert.Equal(1,router.Adds);
    }
    [Theory]
    [InlineData("hotspot")][InlineData("pppoe")][InlineData("userman")]
    public async Task AConflictingReadbackIsUnknownAndNeverReissued(string service)
    {
        using var store=new VoucherStore(_root);var router=new VoucherPipelineRouter{WrongProfile=true};
        using var operation=new VoucherPipelineOperation(pacer:new VoucherCommandPacer(router.Clock));
        var gateway=new RouterGateway(router);var card=Card(await gateway.ReadRouterIdentityAsync(),service:service);store.SaveBatch([card]);
        Assert.True((await new VoucherBatchDispatcher(gateway,store).SendAsync([card])).UncertainOutcome);
        Assert.Equal("UNKNOWN",Assert.Single(store.GetVouchers()).DeliveryStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new VoucherBatchDispatcher(gateway,store).SendAsync([card]));
        Assert.Equal(1,router.Adds);Assert.Equal(1,router.Verifications);
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
