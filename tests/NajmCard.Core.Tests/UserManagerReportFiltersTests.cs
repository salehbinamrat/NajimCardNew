using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerReportFiltersTests
{
    private static UmSessionRow Session(bool? active, DateTime when) =>
        new("*A", "client", when.ToString("yyyy-MM-dd HH:mm:ss"), "",
            "1h", 100, 200, "10.0.0.5", "AA:BB", active, when, "");

    private static UmApprovedPayment Payment(decimal amount, string profile,
        DateTime? when=null, string currency="YER") =>
        new("*B", amount, currency, when, profile);

    [Fact]
    public void ActiveSessionCount_IsScopedToTheDisplayedPeriod()
    {
        var days=new[] {
            Session(true,new DateTime(2026,11,10)),
            Session(null,new DateTime(2026,11,12))
        };
        var today=days.Where(x=>x.StartedAt?.Date==new DateTime(2026,11,12))
            .ToArray();
        var counts=UserManagerReportFilters.CountActiveSessions(today);
        Assert.Equal(1,counts.Total);
        Assert.Equal(0,counts.Known);
        Assert.Equal(0,counts.Active);
        Assert.False(counts.IsComplete);
    }

    [Fact]
    public void PartialSessionAccounting_DoesNotClaimCompleteZero()
    {
        var summary=UserManagerReportFilters.CountActiveSessions(
            [Session(true,new DateTime(2026,11,12)),
             Session(null,new DateTime(2026,11,12))]);
        Assert.Equal(1,summary.Active);
        Assert.Equal(1,summary.Known);
        Assert.Equal(2,summary.Total);
        Assert.False(summary.IsComplete);
    }

    [Fact]
    public void SelectedPackage_RestrictsOnlyPaymentsWithMatchingHistoricalProfile()
    {
        var payments=new[] {
            Payment(110,"gold",new DateTime(2026,11,12)),
            Payment(220,"silver",new DateTime(2026,11,12)),
            Payment(35,"gold",new DateTime(2026,11,10))
        };
        var data=UserManagerReportFilters.SelectApprovedPayments(
            payments,new DateTime(2026,11,12),new DateTime(2026,11,12),"gold");
        Assert.True(data.IsComplete);
        Assert.Single(data.Rows);
        Assert.Equal(110,data.Rows[0].Amount);
    }

    [Fact]
    public void UnknownPaymentProfile_PreventsFalseCompletePackageRevenue()
    {
        var payments=new[] {
            Payment(100,"gold",new DateTime(2026,11,12)),
            Payment(25,"غير متاح",new DateTime(2026,11,12))
        };
        var data=UserManagerReportFilters.SelectApprovedPayments(
            payments,null,null,"gold");
        Assert.False(data.IsComplete);
        Assert.True(data.HasUnknownProfile);
        Assert.Single(data.Rows);
    }

    [Fact]
    public void UndatedApprovedPayment_DoesNotBecomeVerifiedDailyRevenue()
    {
        var payments=new[] {
            Payment(100,"gold",new DateTime(2026,11,12)),
            Payment(200,"gold")
        };
        var day=UserManagerReportFilters.SelectApprovedPayments(
            payments,new DateTime(2026,11,12),new DateTime(2026,11,12),null);
        Assert.True(day.HasUndatedPayments);
        Assert.False(day.IsComplete);
        Assert.Equal(100,Assert.Single(day.Rows).Amount);
        var all=UserManagerReportFilters.SelectApprovedPayments(
            payments,null,null,null);
        Assert.True(all.IsComplete);
        Assert.Equal(2,all.Rows.Count);
        Assert.Equal(300,all.Rows.Sum(x=>x.Amount));
    }

    [Fact]
    public void UnknownProfileOutsidePeriod_DoesNotPoisonDateScopedRevenue()
    {
        var result=UserManagerReportFilters.SelectApprovedPayments(
            [Payment(5,"غير متاح",new DateTime(2026,11,9)),
             Payment(40,"gold",new DateTime(2026,11,12))],
            new DateTime(2026,11,12),new DateTime(2026,11,12),"gold");
        Assert.True(result.IsComplete);
        Assert.Equal(40,Assert.Single(result.Rows).Amount);
    }

    [Fact]
    public void ReversedDateRange_IsRejectedRatherThanDisplayed()
    {
        Assert.Throws<ArgumentException>(()=>
            UserManagerReportFilters.SelectApprovedPayments([],
                new DateTime(2026,11,12),new DateTime(2026,11,11),null));
    }
}
