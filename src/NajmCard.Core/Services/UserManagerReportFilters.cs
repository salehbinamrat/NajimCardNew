using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Pure, testable report selectors. A date-scoped zero is not certified
/// merely because an unrelated historical record has a known RADIUS state.
/// Likewise, unknown payment date/profile must not become invented revenue.
/// </summary>
public static class UserManagerReportFilters
{
    public sealed record TrafficRow(UmSessionRow Session,long? PeriodBytes,string Attribution);
    public sealed record TrafficSelection(IReadOnlyList<TrafficRow> Rows,
        IReadOnlyDictionary<DateTime,long> DailyBytes,int UncertainDateCount,
        bool HasUnknownProfile,int UnallocatedDailyCount)
    {
        public int KnownCount=>Rows.Count(x=>x.PeriodBytes.HasValue);
        public long KnownBytes=>Rows.Where(x=>x.PeriodBytes.HasValue).Sum(x=>x.PeriodBytes!.Value);
        public bool IsComplete=>!HasUnknownProfile&&UncertainDateCount==0&&KnownCount==Rows.Count;
    }

    /// <summary>
    /// Router-local, end-exclusive accounting intervals. Counters describe the
    /// WHOLE session; duration is not evidence of how bytes were distributed.
    /// Only complete intervals contained in the selected period are attributable.
    /// Separate same-day accounting records support daily totals; an unsplit
    /// midnight-spanning record remains unallocated in the daily chart.
    /// </summary>
    public static TrafficSelection SelectTraffic(IReadOnlyList<UmSessionRow> sessions,
        DateTime? from,DateTime? to,string? profile)
    {
        if(from?.Date>to?.Date)throw new ArgumentException("تاريخ البداية بعد تاريخ النهاية.");
        var lower=from?.Date;
        var upper=to?.Date==DateTime.MaxValue.Date?null:to?.Date.AddDays(1);
        bool scoped=from.HasValue||to.HasValue,profileFilter=!string.IsNullOrWhiteSpace(profile);
        var rows=new List<TrafficRow>();var daily=new SortedDictionary<DateTime,long>();
        int unknownDate=0,unallocated=0;bool unknownProfile=false;
        foreach(var session in sessions)
        {
            var start=session.StartedAt;
            var end=RouterGateway.ParseUmDate(session.Ended);
            // Date-only fields cannot prove an exact midnight boundary.
            bool interval=start.HasValue&&end.HasValue&&end>start&&session.IsActive==false&&
                session.Started.Contains(':')&&session.Ended.Contains(':');
            if(scoped)
            {
                if(start.HasValue&&upper.HasValue&&start>=upper)continue;
                if(interval&&lower.HasValue&&end<=lower)continue;
            }
            // RouterOS session records do not establish a historical profile.
            // In particular, never join to the account's CURRENT profile.
            if(profileFilter){unknownProfile=true;continue;}
            if(scoped&&!start.HasValue){unknownDate++;continue;}
            bool contained=interval&&(!lower.HasValue||start>=lower)&&(!upper.HasValue||end<=upper);
            long? bytes=!scoped||contained?session.TotalBytes:null;
            if(scoped&&end.HasValue&&start.HasValue&&end<=start)bytes=null;
            bool sameDay=interval&&start!.Value.Date==end!.Value.AddTicks(-1).Date;
            string reason=bytes is null?"استهلاك الفترة غير معروف":
                !sameDay?"إجمالي مثبت؛ التوزيع اليومي غير معروف":"استهلاك مثبت داخل يوم واحد";
            rows.Add(new TrafficRow(session,bytes,reason));
            if(bytes.HasValue&&sameDay)
            {
                var date=start!.Value.Date;
                daily[date]=checked(daily.GetValueOrDefault(date)+bytes.Value);
            }
            else unallocated++;
        }
        return new TrafficSelection(rows,daily,unknownDate,unknownProfile,unallocated);
    }

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
