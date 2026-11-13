using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerV6PackageUsageTests
{
    private static UserManagerV6Voucher Card(string profile, bool started) =>
        new("*1", "card", profile, "valid", "", "", 0, 0, 0,
            started, false, false);

    [Fact]
    public void Counts_Only_Started_Cards_Per_Package()
    {
        var inventory = new UserManagerV6Inventory(
            [Card("Gold", true), Card("gold", true), Card("Gold", false),
             Card("Silver", true), Card("", true)], true, "");
        var result = UserManagerV6PackageUsage.CountUsed(inventory);
        Assert.Equal(2, result.Count);
        Assert.Equal("Gold", result[0].Name);
        Assert.Equal(2, result[0].Used);
        Assert.Equal("Silver", result[1].Name);
        Assert.Equal(1, result[1].Used);
    }

    [Fact]
    public void Incomplete_Inventory_Cannot_Be_Used_For_Statistics()
    {
        var inventory = new UserManagerV6Inventory([Card("Gold", true)], false, "partial");
        Assert.Throws<InvalidDataException>(() => UserManagerV6PackageUsage.CountUsed(inventory));
    }
}
