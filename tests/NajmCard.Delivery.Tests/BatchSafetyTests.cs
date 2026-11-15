using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Delivery.Tests;

public sealed class BatchSafetyTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"najm-batch-safety",Guid.NewGuid().ToString("N"));
    private VoucherStore Store()=>new(_root);
    private static Dictionary<string,string> Row(params string[] pairs)=>Enumerable.Range(0,pairs.Length/2)
        .ToDictionary(i=>pairs[i*2],i=>pairs[i*2+1],StringComparer.Ordinal);
    private sealed class Router(string serial="device-a") : IRouterCommandClient,IRouterReceiptClient,
        IRouterQueryClient,IRouterNameQueryClient,IAsyncDisposable
    {
        public string Serial=serial;
        public string? DeniedPath;
        public bool UserManagerUnavailable;
        public bool DropActivation,ApplyActivation=true,DropAdd,ApplyAdd=true,Disposed;
        public readonly List<string> Writes=[];
        public readonly Dictionary<string,List<Dictionary<string,string>>> Users=new()
        { ["/ip/hotspot/user/print"]=[],["/ppp/secret/print"]=[],["/tool/user-manager/user/print"]=[] };
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();if(Disposed)throw new ObjectDisposedException("router");
            if(command==DeniedPath)throw new RouterOsTrapException("not enough permissions");
            if(command.EndsWith("/print"))
            {
                IReadOnlyList<IReadOnlyDictionary<string,string>> rows=command switch
                {
                    "/system/routerboard/print"=>[Row("serial-number",Serial)],
                    "/system/resource/print"=>[Row("version","6.49.19")],
                    "/system/license/print"=>[],
                    "/tool/user-manager/profile/print"=>[Row(".id","*F","name","gold","owner","admin")],
                    "/ip/hotspot/print"=>[Row("name","hs","profile","hsprof","disabled","no")],
                    "/ip/hotspot/profile/print"=>[Row("name","hsprof","use-radius","yes","radius-accounting","yes")],
                    "/radius/print"=>[Row("service","hotspot","address","127.0.0.1","disabled","no")],
                    "/tool/user-manager/router/print"=>[Row("name","local","ip-address","127.0.0.1","disabled","no")],
                    _ when Users.TryGetValue(command,out var users)=>users,
                    _=>[]
                };
                return Task.FromResult(rows);
            }
            if(command.EndsWith("/add")){Add(command,attributes!);return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);}
            Writes.Add(command);
            if(command=="/tool/user-manager/user/create-and-activate-profile")
            {
                if(ApplyActivation)Users["/tool/user-manager/user/print"].Single(x=>x[".id"]==attributes!["numbers"])["actual-profile"]=attributes!["profile"];
                if(DropActivation)throw new IOException("activation acknowledgement lost");
            }
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
        private string Add(string command,IReadOnlyDictionary<string,string> attributes)
        {
            Writes.Add(command);var path=command[..^3]+"print";
            var id="*"+(Users.Values.Sum(x=>x.Count)+1).ToString("X");
            if(ApplyAdd)
            {
                var row=new Dictionary<string,string>(attributes){[".id"]=id,["disabled"]="false",["incomplete"]="false"};
                if(command.Contains("user-manager"))row["actual-profile"]="";
                Users[path].Add(row);
            }
            if(DropAdd)throw new IOException("add acknowledgement lost");
            return id;
        }
        public Task<RouterCommandReply> CommandWithReceiptAsync(string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)=>Task.FromResult(new RouterCommandReply([],Row("ret",Add(command,attributes!))));
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByNameAsync(string command,string name,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            if(UserManagerUnavailable&&command.Contains("user-manager"))throw new RouterOsTrapException("no such command");
            if(command==DeniedPath)throw new RouterOsTrapException("not enough permissions");
            var field=command.Contains("/tool/user-manager/")?"username":"name";
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(Users[command]
                .Where(r=>r.GetValueOrDefault(field)==name).Select(r=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>(r)).ToArray());
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(string command,string id,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)=>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(Users[command].Where(r=>r[".id"]==id).ToArray());
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
    }
    private static async Task<Voucher> Card(RouterGateway gateway,string service="hotspot",string name="card1",string batch="batch")=>
        new Voucher(name,"","gold",batch,DateTimeOffset.UtcNow,Service:service,Customer:service=="userman"?"admin":"")
        {RouterId=await gateway.ReadRouterIdentityAsync()};

    [Fact]
    public async Task SwitchingRoutersBlocksSendingAndReviewWithoutChangingRows()
    {
        using var store=Store();var a=new RouterGateway(new Router());var wrong=new Router("device-b");var b=new RouterGateway(wrong);
        var card=await Card(a);store.SaveBatch([card]);
        await Assert.ThrowsAsync<RouterIdentityMismatchException>(()=>new VoucherBatchDispatcher(b,store).SendAsync([card]));
        await Assert.ThrowsAsync<RouterIdentityMismatchException>(()=>new VoucherBatchRecovery(b,store).ReviewAsync(card.RouterId,card.Service,card.BatchId));
        Assert.Empty(wrong.Writes);Assert.Equal("CREATED",Assert.Single(store.GetVouchers()).DeliveryStatus);
    }
    [Fact]
    public async Task SameCardAndBatchNamesHaveIndependentLeasesAndHistoriesAcrossRouters()
    {
        using var store=Store();var a=await Card(new RouterGateway(new Router()));var b=await Card(new RouterGateway(new Router("device-b")));
        store.SaveBatch([a,b]);var lease=store.BeginSend(a);store.FinishSend(lease,"SENT");
        Assert.True(Assert.Single(store.GetBatchVouchers(a.Service,a.BatchId,a.RouterId)).SentToRouter);
        Assert.Equal("CREATED",Assert.Single(store.GetBatchVouchers(b.Service,b.BatchId,b.RouterId)).DeliveryStatus);
        Assert.Equal(3,store.ReadDeliveryEvents(a.RouterId,a.Service,a.BatchId).Count);
        Assert.Single(store.ReadDeliveryEvents(b.RouterId,b.Service,b.BatchId));
        Assert.Throws<InvalidOperationException>(()=>store.FinishSend(lease with{RouterId=b.RouterId},"SENT"));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LostActivationAcknowledgementNeverRecreatesOrReactivatesTheAccount(bool activated)
    {
        using var store=Store();var router=new Router{DropActivation=true,ApplyActivation=activated};var gateway=new RouterGateway(router);
        var card=await Card(gateway,"userman");store.SaveBatch([card]);
        var result=await new VoucherBatchDispatcher(gateway,store).SendAsync([card]);
        Assert.True(result.UncertainOutcome);Assert.Equal(2,router.Writes.Count);
        using var reopened=Store();
        var recovery=new VoucherBatchRecovery(gateway,reopened);
        Assert.Equal("UNKNOWN",Assert.Single(reopened.GetVouchers()).DeliveryStatus);
        var reviewed=Assert.Single(await recovery.ReviewAsync(card.RouterId,card.Service,card.BatchId));
        Assert.Equal(activated?"SENT":"UNKNOWN",reviewed.DeliveryStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>recovery.ResumeAsync(card.RouterId,card.Service,card.BatchId));
        Assert.Equal(2,router.Writes.Count);Assert.Single(router.Users["/tool/user-manager/user/print"]);
    }
    [Theory]
    [InlineData("hotspot",true)] [InlineData("hotspot",false)]
    [InlineData("pppoe",true)] [InlineData("pppoe",false)]
    public async Task LostCreateReplyIsReconciledBeforeResumingOnlyProvenAbsentCards(string service,bool created)
    {
        using var store=Store();var router=new Router{DropAdd=true,ApplyAdd=created};var gateway=new RouterGateway(router);
        var card=await Card(gateway,service);store.SaveBatch([card]);
        Assert.True((await new VoucherBatchDispatcher(gateway,store).SendAsync([card])).UncertainOutcome);
        using var reopened=Store();var recovery=new VoucherBatchRecovery(gateway,reopened);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>recovery.ResumeAsync(card.RouterId,service,card.BatchId));
        var reviewed=Assert.Single(await recovery.ReviewAsync(card.RouterId,service,card.BatchId));
        Assert.Equal(created?"SENT":"READY",reviewed.DeliveryStatus);
        router.DropAdd=false;router.ApplyAdd=true;
        if(!created)Assert.True((await recovery.ResumeAsync(card.RouterId,service,card.BatchId)).Success);
        Assert.Equal(created?1:2,router.Writes.Count);
        Assert.True(Assert.Single(reopened.GetVouchers()).SentToRouter);
    }
    [Fact]
    public async Task CrashAfterLeaseBeforeNetworkRequiresReviewAndDoesNotReissueCompletedCards()
    {
        var router=new Router();var gateway=new RouterGateway(router);var first=await Card(gateway);
        var second=first with{Username="card2"};
        using(var store=Store())
        {
            store.SaveBatch([first,second]);
            await new VoucherBatchDispatcher(gateway,store).SendAsync([first]);
            store.BeginSend(second); // Process dies with no network request.
        }
        using var reopened=Store();var recovery=new VoucherBatchRecovery(gateway,reopened);
        Assert.Throws<InvalidOperationException>(()=>reopened.BeginSend(second));
        var rows=await recovery.ReviewAsync(first.RouterId,first.Service,first.BatchId);
        Assert.Equal("SENT",rows.Single(x=>x.Username==first.Username).DeliveryStatus);
        Assert.Equal("READY",rows.Single(x=>x.Username==second.Username).DeliveryStatus);
        await recovery.ResumeAsync(first.RouterId,first.Service,first.BatchId);
        Assert.Equal(2,router.Writes.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>recovery.ResumeAsync(first.RouterId,first.Service,first.BatchId));
    }
    [Theory]
    [InlineData("/ip/hotspot/user/print")]
    [InlineData("/ppp/secret/print")]
    [InlineData("/tool/user-manager/user/print")]
    public async Task IncompleteReadCannotProveAbsence(string denied)
    {
        using var store=Store();var router=new Router{DeniedPath=denied};var gateway=new RouterGateway(router);
        var card=await Card(gateway);store.SaveBatch([card]);store.BeginSend(card);
        var recovery=new VoucherBatchRecovery(gateway,store);
        Assert.Equal("UNKNOWN",Assert.Single(await recovery.ReviewAsync(card.RouterId,card.Service,card.BatchId)).DeliveryStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>recovery.ResumeAsync(card.RouterId,card.Service,card.BatchId));
        Assert.Empty(router.Writes);
    }
    [Theory]
    [InlineData("marker")][InlineData("profile")][InlineData("cross-service")]
    public async Task MatchingNameAloneDoesNotConfirmDelivery(string fault)
    {
        using var store=Store();var router=new Router();var gateway=new RouterGateway(router);
        var card=await Card(gateway);store.SaveBatch([card]);store.BeginSend(card);
        var row=Row(".id","*1","name",card.Username,"comment",card.DeliveryTag,"profile",card.Profile,"disabled","false");
        if(fault=="marker")row["comment"]="someone else's card";
        if(fault=="profile")row["profile"]="other";
        router.Users[fault=="cross-service"?"/ppp/secret/print":"/ip/hotspot/user/print"].Add(row);
        Assert.Equal("CONFLICT",Assert.Single(await new VoucherBatchRecovery(gateway,store).ReviewAsync(card.RouterId,card.Service,card.BatchId)).DeliveryStatus);
        Assert.Empty(router.Writes);
    }
    [Fact]
    public async Task ACollisionAppearingAfterReviewBlocksResume()
    {
        using var store=Store();var router=new Router();var gateway=new RouterGateway(router);
        var card=await Card(gateway);store.SaveBatch([card]);store.BeginSend(card);
        var recovery=new VoucherBatchRecovery(gateway,store);
        await recovery.ReviewAsync(card.RouterId,card.Service,card.BatchId);
        router.Users["/ip/hotspot/user/print"].Add(Row(".id","*1","name",card.Username));
        await Assert.ThrowsAsync<ArgumentException>(()=>recovery.ResumeAsync(card.RouterId,card.Service,card.BatchId));
        Assert.Empty(router.Writes);Assert.Equal("READY",Assert.Single(store.GetVouchers()).DeliveryStatus);
    }
    [Fact]
    public async Task ConcurrentReviewCannotUnlockALiveSendingLease()
    {
        using var store=Store();var router=new Router();var gateway=new RouterGateway(router);
        var card=await Card(gateway);store.SaveBatch([card]);
        using var operation=store.LockBatch(card.RouterId,card.Service,card.BatchId);store.BeginSend(card);
        using var other=Store();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new VoucherBatchRecovery(gateway,other).ReviewAsync(card.RouterId,card.Service,card.BatchId));
        Assert.Equal("SENDING",Assert.Single(store.GetVouchers()).DeliveryStatus);
    }
    [Fact]
    public async Task OldReceiptCannotCompleteANewerAttempt()
    {
        using var store=Store();var card=await Card(new RouterGateway(new Router()));store.SaveBatch([card]);
        var old=store.BeginSend(card);store.FinishSend(old,"FAILED");var current=store.BeginSend(card);
        Assert.NotEqual(old.AttemptId,current.AttemptId);
        Assert.Throws<InvalidOperationException>(()=>store.FinishSend(old,"SENT"));
        store.FinishSend(current,"SENT");
        var events=store.ReadDeliveryEvents(card.RouterId,card.Service,card.BatchId);
        Assert.Equal(new[]{"CREATED","SENDING","FAILED","SENDING","SENT"},events.Select(x=>x.State));
        Assert.All(events,x=>Assert.True(DateTimeOffset.TryParse(x.Timestamp,out _)));
    }
    [Fact]
    public async Task ReconnectionToAnotherDeviceIsRejectedBeforeReadsOrWrites()
    {
        var original=new Router();var replacement=new Router("device-b");
        await using var session=new ResilientRouterSession(original,_=>Task.FromResult<IRouterCommandClient>(replacement));
        await new RouterGateway(session).ReadRouterIdentityAsync();original.Disposed=true;
        await Assert.ThrowsAsync<RouterIdentityMismatchException>(()=>session.CommandAsync("/system/resource/print"));
        await Assert.ThrowsAsync<RouterIdentityMismatchException>(()=>session.CommandAsync("/ip/hotspot/user/add",Row("name","card")));
        Assert.Empty(replacement.Writes);
    }
    [Fact]
    public async Task InventoryLaneAlsoRejectsAnotherDevice()
    {
        var original=new Router();var replacement=new Router("device-b");
        await using var session=new ResilientRouterSession(original,_=>Task.FromResult<IRouterCommandClient>(replacement));
        await new RouterGateway(session).ReadRouterIdentityAsync();
        await Assert.ThrowsAsync<RouterIdentityMismatchException>(()=>session.CommandAsync("/tool/user-manager/user/print"));
    }
    [Fact]
    public async Task RouterIdentityRemainsStableAcrossNewConnectionsToTheSameDevice()
    {
        var original=new Router();var replacement=new Router();
        await using var session=new ResilientRouterSession(original,_=>Task.FromResult<IRouterCommandClient>(replacement));
        var gateway=new RouterGateway(session);var identity=await gateway.ReadRouterIdentityAsync();original.Disposed=true;
        Assert.Equal(identity,await gateway.ReadRouterIdentityAsync());
    }
    [Theory]
    [InlineData("CREATED",0)][InlineData("SENDING",0)][InlineData("UNKNOWN",0)][InlineData("SENT",1)]
    public void MigrationPreservesEncryptedPayloadAndOtherTablesWithoutInventingOwnership(string state,int sent)
    {
        Directory.CreateDirectory(_root);
        using(var db=new SqliteConnection($"Data Source={Path.Combine(_root,"najmcard.db")};Pooling=False"))
        {
            db.Open();using var cmd=db.CreateCommand();cmd.CommandText="""
                CREATE TABLE vouchers(service TEXT,username TEXT,password_protected TEXT,customer TEXT,
                    profile TEXT,batch_id TEXT,created_at TEXT,sent INTEGER,delivery_status TEXT,last_error TEXT,
                    PRIMARY KEY(service,username));
                INSERT INTO vouchers VALUES('hotspot','old','opaque DPAPI bytes','','gold','old-batch','2020-01-01T00:00:00Z',$sent,$state,'original error');
                CREATE TABLE app_preferences(key TEXT PRIMARY KEY,value TEXT);
                INSERT INTO app_preferences VALUES('setting','keep this');
                CREATE TABLE license_sentinel(payload TEXT);INSERT INTO license_sentinel VALUES('untouched license');
                """;
            cmd.Parameters.AddWithValue("$sent",sent);cmd.Parameters.AddWithValue("$state",state);cmd.ExecuteNonQuery();
        }
        File.WriteAllText(Path.Combine(_root,"license.dat"),"license file unchanged");
        using(var store=Store()){}
        using(var reopened=Store()){}
        using var migrated=new SqliteConnection($"Data Source={Path.Combine(_root,"najmcard.db")};Pooling=False");migrated.Open();
        using var query=migrated.CreateCommand();query.CommandText="SELECT router_id,password_protected,delivery_status,sent,last_error FROM vouchers";
        using(var reader=query.ExecuteReader())
        {
            Assert.True(reader.Read());Assert.Equal("",reader.GetString(0));Assert.Equal("opaque DPAPI bytes",reader.GetString(1));
            Assert.Equal(state,reader.GetString(2));Assert.Equal(sent,reader.GetInt32(3));Assert.Equal("original error",reader.GetString(4));
        }
        query.CommandText="SELECT value FROM app_preferences";Assert.Equal("keep this",query.ExecuteScalar());
        query.CommandText="SELECT payload FROM license_sentinel";Assert.Equal("untouched license",query.ExecuteScalar());
        query.CommandText="SELECT COUNT(*) FROM voucher_delivery_events";Assert.Equal(1L,query.ExecuteScalar());
        Assert.Equal("license file unchanged",File.ReadAllText(Path.Combine(_root,"license.dat")));
        Assert.Single(Directory.GetFiles(Path.Combine(_root,"Backups"),"before-router-v2-*.db"));
    }
    [Fact]
    public void FailedMigrationRollsBackAndKeepsTheOriginalRows()
    {
        Directory.CreateDirectory(_root);
        using var db=new SqliteConnection($"Data Source={Path.Combine(_root,"najmcard.db")};Pooling=False");db.Open();
        using var cmd=db.CreateCommand();cmd.CommandText="""
            CREATE TABLE vouchers(username TEXT PRIMARY KEY,password_protected TEXT,profile TEXT,batch_id TEXT,created_at TEXT,sent INTEGER);
            INSERT INTO vouchers VALUES('bad','opaque',NULL,'batch','2020-01-01',0);
            """;cmd.ExecuteNonQuery();
        Assert.Throws<SqliteException>(()=>Store());
        cmd.CommandText="SELECT password_protected FROM vouchers WHERE username='bad'";Assert.Equal("opaque",cmd.ExecuteScalar());
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name='vouchers_router_v2'";Assert.Equal(0L,cmd.ExecuteScalar());
    }

    [Fact]
    public async Task AmbiguousBatchQueriesAndMixedExportsMustNotCombineRouters()
    {
        using var store=Store();
        var a=(await Card(new RouterGateway(new Router()))) with{SentToRouter=true,DeliveryStatus="SENT"};
        var b=(await Card(new RouterGateway(new Router("device-b")))) with{SentToRouter=true,DeliveryStatus="SENT"};
        store.SaveBatch([a,b]);
        Assert.Equal(2,store.ListPrintableBatches().Count);
        Assert.Equal(2,store.ListDeliveryBatches().Count);
        Assert.Throws<InvalidOperationException>(()=>store.GetBatchVouchers(a.Service,a.BatchId));
        Assert.Throws<ArgumentException>(()=>VoucherBatchCsvExporter.Export([a,b],Path.Combine(_root,"mixed.csv")));
        Assert.Throws<ArgumentException>(()=>VoucherBatchArchive.CreateDirectory(_root,[a,b]));
    }

    [Fact]
    public async Task SavedPrintSnapshotsRemainIndependentWhenRouterBatchNamesMatch()
    {
        using var store=Store();var a=await Card(new RouterGateway(new Router()));var b=await Card(new RouterGateway(new Router("device-b")));
        VoucherBatchPrintSnapshot Snapshot(Voucher c,string title)=>new(c.Service,c.BatchId,c.Profile,c.Customer,
            new CardPrintOptions(),new VoucherCardAppearance(NetworkTitle:title)) {RouterId=c.RouterId};
        store.SaveBatch([a],Snapshot(a,"router A"));store.SaveBatch([b],Snapshot(b,"router B"));
        Assert.Equal("router A",store.LoadBatchPrintSnapshot(a.Service,a.BatchId,a.RouterId)!.Appearance.NetworkTitle);
        Assert.Equal("router B",store.LoadBatchPrintSnapshot(b.Service,b.BatchId,b.RouterId)!.Appearance.NetworkTitle);
        Assert.Throws<InvalidOperationException>(()=>store.LoadBatchPrintSnapshot(a.Service,a.BatchId));
    }

    [Fact]
    public async Task AbsentUserManagerPackageDoesNotBlockHotspotReconciliation()
    {
        using var store=Store();var router=new Router{UserManagerUnavailable=true};var gateway=new RouterGateway(router);
        var card=await Card(gateway);store.SaveBatch([card]);store.BeginSend(card);
        var row=Assert.Single(await new VoucherBatchRecovery(gateway,store).ReviewAsync(card.RouterId,card.Service,card.BatchId));
        Assert.Equal("READY",row.DeliveryStatus);Assert.Empty(router.Writes);
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
