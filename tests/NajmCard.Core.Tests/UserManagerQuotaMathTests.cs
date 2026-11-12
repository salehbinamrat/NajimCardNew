using System.Globalization;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerQuotaMathTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(1024L)]
    [InlineData(1073741823L)]
    [InlineData(1073741824L)]
    [InlineData(1073741825L)]
    [InlineData(1099511627776L)]
    [InlineData(9223372036854775807L)]
    public void ExistingRouterByteQuota_RoundTripsWithoutChangingAnyByte(long original)
    {
        string displayed=UserManagerQuotaMath.ToEditorGiB(original);
        string saved=UserManagerQuotaMath.ParseGiBToByteString(displayed);
        Assert.Equal(original.ToString(CultureInfo.InvariantCulture),saved);
    }

    [Fact]
    public void UserSuppliedHalfGiB_UsesExactByteCount()
    {
        Assert.Equal("536870912",UserManagerQuotaMath.ParseGiBToByteString("0.5"));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("99999999999999999")]
    [InlineData("NaN")]
    [InlineData("1GB")]
    public void InvalidQuota_IsRejectedWithoutChangingRouter(string input)
    {
        Assert.Throws<ArgumentException>(()=>
            UserManagerQuotaMath.ParseGiBToByteString(input));
    }

    [Fact]
    public void BlankQuota_RemainsUnspecified_NotAnAccidentalZeroLimit()
    {
        Assert.Equal("",UserManagerQuotaMath.ParseGiBToByteString(""));
    }
}
