using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Delivery.Tests;

public sealed class RouterBatchRecoveryTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"najm-delivery-tests",Guid.NewGuid().ToString("N"));
    private VoucherStore Store()=>new(_root);
    private static Voucher Card(string router="",string name="123456",string batch="batch-1")
    {
        var card=new Voucher(name,"","gold",batch,DateTimeOffset.UtcNow) {RouterId=router};
        return card;
    }
    private sealed class Router : IRouterCommandClient
    {
        public int Writes;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            if(!command.EndsWith("/print",StringComparison.Ordinal))Writes++;
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }
    [Fact]
    public void EqualNamesOnDifferentRoutersRemainSeparate()
    {
        using var store=Store();
        store.SaveBatch([Card("router-a")]);
        store.SaveBatch([Card("router-b")]);
        Assert.Equal(2,store.GetVouchers().Count);
    }
    [Fact]
    public void LegacyUnknownOwnershipCannotAcquireASendLease()
    {
        using var store=Store();var card=Card();store.SaveBatch([card]);
        Assert.Throws<InvalidOperationException>(()=>store.BeginSend(card));
    }
    [Fact]
    public async Task LegacyUnknownOwnershipCannotBeSentOnTheCurrentRouter()
    {
        using var store=Store();var card=Card();store.SaveBatch([card]);var router=new Router();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new VoucherBatchDispatcher(new RouterGateway(router),store).SendAsync([card]));
        Assert.Equal(0,router.Writes);
    }
    [Fact]
    public void MigrationRecordsRouterOwnershipWithoutAssigningLegacyRows()
    {
        using var store=Store();store.SaveBatch([Card()]);
        using var db=new SqliteConnection($"Data Source={Path.Combine(_root,"najmcard.db")};Pooling=False");db.Open();
        using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA table_info(vouchers)";
        using var rows=cmd.ExecuteReader();var columns=new List<string>();
        while(rows.Read())columns.Add(rows.GetString(1));
        Assert.Contains("router_id",columns);
    }

    [Fact]
    public async Task UnavailableStableIdentityCannotAuthorizeDelivery()
    {
        using var store=Store();var card=Card("router-a");store.SaveBatch([card]);var router=new Router();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>
            new VoucherBatchDispatcher(new RouterGateway(router),store).SendAsync([card]));
        Assert.Equal(0,router.Writes);
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
