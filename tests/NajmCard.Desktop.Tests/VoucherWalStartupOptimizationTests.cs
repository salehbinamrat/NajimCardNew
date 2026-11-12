using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class VoucherWalStartupOptimizationTests
{
    [Fact]
    public void WalIsPersistedAcrossFreshQueriesWithoutLosingFullSync()
    {
        string root=Path.Combine(Path.GetTempPath(),"najm-wal-check-"+
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store=new VoucherStore(root);
            var card=new Voucher("123456","","1d","batch-1",
                DateTimeOffset.UtcNow,Service:"hotspot");
            store.SaveBatch([card]);

            // The store's read/write operations open many fresh connections.
            // WAL must be set once and stay persistent while FULL sync
            // is configured on each new connection.
            var method=typeof(VoucherStore).GetMethod("Open",
                BindingFlags.NonPublic|BindingFlags.Instance);
            Assert.NotNull(method);
            for(int i=0;i<4;i++)
            {
                using var db=Assert.IsType<SqliteConnection>(
                    method!.Invoke(store,[false]));
                using var cmd=db.CreateCommand();
                cmd.CommandText="PRAGMA journal_mode";
                Assert.Equal("wal",
                    Assert.IsType<string>(cmd.ExecuteScalar()),
                    StringComparer.OrdinalIgnoreCase);
                cmd.CommandText="PRAGMA synchronous";
                Assert.Equal(2L,Convert.ToInt64(cmd.ExecuteScalar()));
            }
            Assert.Single(store.GetVouchers());
            using var reopened=new VoucherStore(root);
            Assert.Single(reopened.GetVouchers());
        }
        finally{Directory.Delete(root,recursive:true);}
    }
}
