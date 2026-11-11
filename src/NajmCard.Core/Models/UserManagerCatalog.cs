namespace NajmCard.Core.Models;

/// <summary>Which RouterOS CLI/API generation was confirmed by /system/resource/print.</summary>
public enum RouterOsGeneration { V6 = 6, V7 = 7 }

public enum UserManagerCatalogSection { Users, Profiles, Limitations, Sessions }

/// <summary>
/// Read-only, credential-free presentation fields. Missing metrics remain unknown,
/// never converted to 0 or interpreted as exhausted quotas.
/// </summary>
public sealed record UserManagerCatalogRow(
    string Id, string Name, string Profile, string Status,
    string Limit, string Uptime, string Download, string Upload,
    string Address, string Extra);

public sealed record UserManagerCatalogSnapshot(
    RouterOsGeneration Generation,
    UserManagerCatalogSection Section,
    IReadOnlyList<UserManagerCatalogRow> Rows);
