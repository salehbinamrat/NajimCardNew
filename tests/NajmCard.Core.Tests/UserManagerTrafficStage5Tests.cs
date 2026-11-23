using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;
namespace NajmCard.Core.Tests;

public sealed class UserManagerTrafficStage5Tests
{
    private static readonly DateTime Day=new(2026,11,22);
    private static UmSessionRow Session(string start,string end,long? down=600,long? up=400) =>
        new("*1","card",start,end,"",down,up,"","",false,RouterGateway.ParseUmDate(start),"");

    [Fact]
    public void EmptyReportExportsUnknownConsumptionLikeTheDisplayedMetric()
    {
        var traffic=UserManagerReportFilters.SelectTraffic([],Day,Day,null);
        var summary=Assert.Single(UserManagerReportCsv.Rows([],traffic,null,Day,Day,null,"fresh"));
        Assert.Equal("غير معروف",summary[13]);
    }

    [Fact]
    public void ExportUsesSelectedProfileAndDateSnapshotWithoutGlobalSessionsOrPayments()
    {
        var traffic=UserManagerReportFilters.SelectTraffic([Session("2026-11-22 01:00:00","2026-11-22 02:00:00")],Day,Day,"gold");
        UmApprovedPayment[] raw=[new("selected",25,"YER",Day,"gold"),
            new("other-profile",50,"YER",Day,"silver"),new("other-date",100,"YER",Day.AddDays(-1),"gold")];
        var paid=UserManagerReportFilters.SelectApprovedPayments(raw,Day,Day,"gold");
        var rows=UserManagerReportCsv.Rows([],traffic,paid,Day,Day,"gold","cached at 12:00").ToArray();
        Assert.DoesNotContain(rows,x=>x[8]=="session");
        var payment=Assert.Single(rows,x=>x[8]=="payment");
        Assert.Equal("selected",payment[18]);Assert.Equal("25",payment[15]);
        Assert.All(rows,x=>{Assert.Equal("gold",x[9]);Assert.Equal("2026-11-22",x[10]);Assert.Equal("cached at 12:00",x[17]);});
        Assert.Contains("غير معروفة",rows[0][14]);
    }

    [Fact]
    public void WholeCrossMidnightSessionIsExactForRangeButNeverSplitByDuration()
    {
        var result=UserManagerReportFilters.SelectTraffic([Session("2026-11-21 23:00:00","2026-11-22 01:00:00")],Day.AddDays(-1),Day,null);
        Assert.Equal(1000L,result.KnownBytes);
        Assert.Equal(1000L,Assert.Single(result.Rows).PeriodBytes);
        Assert.Empty(result.DailyBytes);
        Assert.Equal(1,result.UnallocatedDailyCount);
    }

    [Fact]
    public void OverlapIncludesEarlierStartWithoutAttributingWholeCounterToSelectedDay()
    {
        var result=UserManagerReportFilters.SelectTraffic([Session("2026-11-21 23:00:00","2026-11-22 01:00:00")],Day,Day,null);
        Assert.Null(Assert.Single(result.Rows).PeriodBytes);
        Assert.Equal(0,result.KnownCount);
        Assert.False(result.IsComplete);
        Assert.Empty(result.DailyBytes);
    }

    [Fact]
    public void ExactMidnightEndBelongsOnlyToPreviousDay()
    {
        var row=Session("2026-11-21 23:00:00","2026-11-22 00:00:00");
        Assert.Empty(UserManagerReportFilters.SelectTraffic([row],Day,Day,null).Rows);
        var before=UserManagerReportFilters.SelectTraffic([row],Day.AddDays(-1),Day.AddDays(-1),null);
        Assert.Equal(1000L,before.DailyBytes[Day.AddDays(-1)]);
    }

    [Fact]
    public void IndependentDailyAccountingRecordsProvideSupportedDailyAllocation()
    {
        var rows=new[]{Session("2026-11-21 23:00:00","2026-11-22 00:00:00",200,100),
            Session("2026-11-22 00:00:00","2026-11-22 01:00:00",500,200) with {Id="*2"}};
        var result=UserManagerReportFilters.SelectTraffic(rows,null,null,null);
        Assert.Equal(300L,result.DailyBytes[Day.AddDays(-1)]);
        Assert.Equal(700L,result.DailyBytes[Day]);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void UnknownOrReversedTimesAndMissingCountersNeverBecomeZero()
    {
        var rows=new[]{Session("",""),Session("2026-11-22 03:00:00","2026-11-22 02:00:00"),
            Session("2026-11-22 01:00:00","2026-11-22 02:00:00",null,5)};
        var result=UserManagerReportFilters.SelectTraffic(rows,Day,Day,null);
        Assert.Equal(1,result.UncertainDateCount);
        Assert.All(result.Rows,x=>Assert.Null(x.PeriodBytes));
        Assert.False(result.IsComplete);
        Assert.Empty(result.DailyBytes);
    }

    [Fact]
    public void ProfileFilterDoesNotLeakGlobalSessionsOrGuessHistoricalProfile()
    {
        var result=UserManagerReportFilters.SelectTraffic([Session("2026-11-22 01:00:00","2026-11-22 02:00:00")],Day,Day,"gold");
        Assert.Empty(result.Rows);
        Assert.True(result.HasUnknownProfile);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void InvalidDateRangeIsRejectedBeforeExport()
        =>Assert.Throws<ArgumentException>(()=>UserManagerReportFilters.SelectTraffic([],Day,Day.AddDays(-1),null));
}
