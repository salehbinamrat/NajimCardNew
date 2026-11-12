using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Pure, testable report selectors. A date-scoped zero is not certified
/// merely because an unrelated historical record has a known RADIUS state.
/// Likewise, unknown payment date/profile must not become invented revenue.
/// </summary>
public static class UserManagerReportFilters
{
    public sealed record ActiveSessions(int Active,int Known,int Total)
    {
        public bool IsComplete => Known==Total && Total>0;
    }

    public sealed record ApprovedPayments(
        IReadOnlyList<UmApprovedPayment> Rows,bool HasUnknownProfile,
        bool HasUndatedPayments)
    {
        public bool IsComplete => !HasUnknownProfile && !HasUndatedPayments;
    }

    public static ActiveSessions CountActiveSessions(
        IReadOnlyList<UmSessionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var active=0;
        var known=0;
        foreach(var row in rows)
        {
            if(!row.IsActive.HasValue)continue;
            known++;
            if(row.IsActive.Value)active++;
        }
        return new ActiveSessions(active,known,rows.Count);
    }

    public static ApprovedPayments SelectApprovedPayments(
        IReadOnlyList<UmApprovedPayment> payments,
        DateTime? from,DateTime? to,string? profile)
    {
        ArgumentNullException.ThrowIfNull(payments);
        if(from.HasValue&&to.HasValue&&from.Value.Date>to.Value.Date)
            throw new ArgumentException("تاريخ البداية بعد تاريخ النهاية.");

        var applyDate=from.HasValue||to.HasValue;
        var applyProfile=!string.IsNullOrWhiteSpace(profile);
        var matching=new List<UmApprovedPayment>(payments.Count);
        bool unknownProfile=false,undated=false;

        foreach(var payment in payments)
        {
            var knownProfile=!string.IsNullOrWhiteSpace(payment.Profile)&&
                !string.Equals(payment.Profile.Trim(),"غير متاح",
                    StringComparison.OrdinalIgnoreCase);
            var date=payment.Date?.Date;
            var withinDate=!applyDate || (date.HasValue &&
                (!from.HasValue||date.Value>=from.Value.Date) &&
                (!to.HasValue||date.Value<=to.Value.Date));

            if(applyProfile)
            {
                if(!knownProfile)
                {
                    // An unknown profile may belong to the selected package.
                    // Only transactions outside a known date range are safe to
                    // exclude without making the selected revenue incomplete.
                    if(withinDate||!date.HasValue)unknownProfile=true;
                    continue;
                }
                if(!string.Equals(payment.Profile.Trim(),profile!.Trim(),
                    StringComparison.OrdinalIgnoreCase))continue;
            }
            if(applyDate&&!date.HasValue)
            {
                // Cannot attribute this approved payment to any date.
                undated=true;
                continue;
            }
            if(!withinDate)continue;
            matching.Add(payment);
        }

        return new ApprovedPayments(matching,unknownProfile,undated);
    }
}
