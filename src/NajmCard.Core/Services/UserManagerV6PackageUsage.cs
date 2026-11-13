using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Pure, local calculation. Never starts a RouterOS query and never treats
/// partial inventory as a trustworthy count.
/// </summary>
public static class UserManagerV6PackageUsage
{
    public sealed record PackageCount(string Name, int Used);

    public static IReadOnlyList<PackageCount> CountUsed(UserManagerV6Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!inventory.IsComplete)
            throw new InvalidDataException("Cannot count package usage from incomplete inventory.");

        return inventory.Users
            .Where(x => !string.IsNullOrWhiteSpace(x.Profile))
            .GroupBy(x => x.Profile, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PackageCount(g.Key, g.Count(x => x.Started)))
            .OrderByDescending(x => x.Used)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
