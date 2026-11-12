using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerRevenueMathTests
{
    private static UmApprovedPayment Payment(decimal amount,string currency)=>
        new("*A",amount,currency,DateTime.Parse("2026-11-12"),"Profile");

    [Fact]
    public void SingleKnownCurrency_CanBeTotalledWithoutFabricatingSales()
    {
        var ok=UserManagerRevenueMath.TryTotalSingleCurrency(
            [Payment(12.5m,"YER"),Payment(40m,"yer")],out var amount,out var currency);
        Assert.True(ok);
        Assert.Equal(52.5m,amount);
        Assert.Equal("YER",currency);
    }

    [Fact]
    public void MixingYemenRiyalsAndDollars_IsNotReportedAsOneRevenue()
    {
        var ok=UserManagerRevenueMath.TryTotalSingleCurrency(
            [Payment(200m,"YER"),Payment(5m,"USD")],out var amount,out _);
        Assert.False(ok);
        Assert.Equal(0m,amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("غير متاح")]
    [InlineData("  ")]
    public void UnknownCurrency_NeverBecomesVerifiedRevenue(string currency)
    {
        Assert.False(UserManagerRevenueMath.TryTotalSingleCurrency(
            [Payment(99m,currency)],out _,out _));
    }

    [Fact]
    public void NoApprovedPayments_IsKnownZeroButNotInventedIncome()
    {
        Assert.True(UserManagerRevenueMath.TryTotalSingleCurrency(
            [],out var amount,out var currency));
        Assert.Equal(0m,amount);
        Assert.Empty(currency);
    }
}
