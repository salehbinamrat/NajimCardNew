using System.IO;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class VoucherDeliveryProgressCacheTests
{
    private static string Temp()
    {
        var folder=Path.Combine(Path.GetTempPath(),
            "najm-batch-progress-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static Voucher Card(int number)=>
        new("UM"+number.ToString("D6"),"", "plan","batch-perf",
            DateTimeOffset.UtcNow,Service:"userman",Customer:"admin");

    [Fact]
    public void MultiCard_FastSession_PreservesAuditCountsAndDurableReceipts()
    {
        var root=Temp();
        try
        {
            using var store=new VoucherStore(root);
            var cards=Enumerable.Range(1,48).Select(Card).ToArray();
            store.SaveBatch(cards);
            using(var session=store.OpenSendSession())
            {
                foreach(var (card,i) in cards.Select((c,i)=>(c,i)))
                {
                    session.BeginSend(card);
                    session.FinishSend(card,i%8==0?"FAILED":"SENT");
                }
            }
            var persisted=store.GetBatchVouchers("userman","batch-perf");
            Assert.Equal(48,persisted.Count);
            Assert.Equal(42,persisted.Count(x=>x.DeliveryStatus=="SENT"));
            Assert.Equal(6,persisted.Count(x=>x.DeliveryStatus=="FAILED"));
            var audit=Assert.Single(store.ReadRecentActivities(8).Where(x=>
                x.Action=="إرسال دفعة كروت"));
            Assert.Equal("PARTIAL",audit.Status);
            Assert.Contains("ناجح 42 من 48",audit.Detail);
            Assert.Contains("فاشل 6",audit.Detail);
            Assert.DoesNotContain("UM000001",audit.Detail);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void ExternalCommit_InvalidatesCachedSummary_BeforeTheNextReceipt()
    {
        var root=Temp();
        try
        {
            using var store=new VoucherStore(root);
            var cards=new[]{Card(1),Card(2),Card(3)};
            store.SaveBatch(cards);
            using(var session=store.OpenSendSession())
            {
                session.BeginSend(cards[0]);
                session.FinishSend(cards[0],"SENT");
                // Another app connection commits a card in the same batch.
                using(var other=new VoucherStore(root))
                {
                    other.BeginSend(cards[2]);
                    other.FinishSend(cards[2],"SENT");
                }
                session.BeginSend(cards[1]);
                session.FinishSend(cards[1],"SENT");
            }
            var audit=Assert.Single(store.ReadRecentActivities(8).Where(x=>
                x.Action=="إرسال دفعة كروت"));
            Assert.Equal("SUCCESS",audit.Status);
            Assert.Contains("ناجح 3 من 3",audit.Detail);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void ManualRetryOfFailedCard_DoesNotDoubleCountTheSameVoucher()
    {
        var root=Temp();
        try
        {
            using var store=new VoucherStore(root);
            var cards=new[]{Card(1),Card(2)};
            store.SaveBatch(cards);
            using(var session=store.OpenSendSession())
            {
                session.BeginSend(cards[0]);
                session.FinishSend(cards[0],"FAILED");
                session.BeginSend(cards[1]);
                session.FinishSend(cards[1],"SENT");
                // Router definitively rejected the first attempt. Its
                // operator-approved retry is legal, without a new card.
                session.BeginSend(cards[0]);
                session.FinishSend(cards[0],"SENT");
            }
            var rows=store.GetBatchVouchers("userman","batch-perf");
            Assert.All(rows,row=>Assert.Equal("SENT",row.DeliveryStatus));
            var audit=Assert.Single(store.ReadRecentActivities(8).Where(x=>
                x.Action=="إرسال دفعة كروت"));
            Assert.Equal("SUCCESS",audit.Status);
            Assert.Contains("ناجح 2 من 2",audit.Detail);
            Assert.Contains("فاشل 0",audit.Detail);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void SendingLease_StaysUnknownAfterCrash_EvenWithCachedTotals()
    {
        var root=Temp();
        try
        {
            var card=Card(1);
            using(var store=new VoucherStore(root))
            {
                store.SaveBatch([card]);
                using var session=store.OpenSendSession();
                session.BeginSend(card);
            }
            using(var store=new VoucherStore(root))
            {
                Assert.Equal("SENDING",Assert.Single(store.GetVouchers()).DeliveryStatus);
                Assert.ThrowsAny<InvalidOperationException>(()=>
                    store.BeginSend(card));
            }
        }
        finally {Directory.Delete(root,true);}
    }
}
