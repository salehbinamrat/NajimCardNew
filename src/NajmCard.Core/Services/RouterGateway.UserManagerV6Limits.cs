using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Legacy User Manager v6 profile/limitation relation inventory.
/// Three small RouterOS6 tables; all links must resolve to avoid trusted but
/// misleading zero-limit results on partial or corrupted responses.
/// </summary>
public sealed partial class RouterGateway
{
    public async Task<IReadOnlyList<UserManagerV6ProfileLimitSummary>>
        ReadUserManagerV6ProfileLimitSummaryAsync(CancellationToken ct = default)
    {
        var catalog=await ReadUserManagerV6QuotaCatalogAsync(ct);
        return catalog.Summaries();
    }
}
