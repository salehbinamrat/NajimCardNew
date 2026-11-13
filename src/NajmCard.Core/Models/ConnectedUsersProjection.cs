namespace NajmCard.Core.Models;

/// <summary>One current connection shown in the operator's read-only online list.
/// "All" includes Hotspot and verified-active User Manager sessions, not PPPoE.</summary>
public sealed record ConnectedUserRow(
    string Source,string User,string Address,string MacAddress,
    string Uptime,string Status);

public static class ConnectedUsersProjection
{
    private static string Known(string? value)=>
        string.IsNullOrWhiteSpace(value)?"غير متاح":value.Trim();

    public static IReadOnlyList<ConnectedUserRow> Hotspot(
        IReadOnlyList<HotspotSession> sessions)=>
        sessions.Select(s=>new ConnectedUserRow(
            "الهوتسبوت",Known(s.User),Known(s.Address),
            Known(s.MacAddress),Known(s.Uptime),"متصل الآن")).ToArray();

    public static IReadOnlyList<ConnectedUserRow> UserManager(
        IReadOnlyList<UmSessionRow> sessions)=>
        // Only a TRUE active flag confirms a live RADIUS session.
        // Unknown, absent and ended status must not be counted as online.
        sessions.Where(s=>s.IsActive==true)
            .Select(s=>new ConnectedUserRow(
                "اليوزر مانجر",Known(s.Name),Known(s.Address),
                Known(s.Mac),Known(s.Uptime),"متصل مؤكد")).ToArray();

    public static IReadOnlyList<ConnectedUserRow> ForView(
        string view,IReadOnlyList<HotspotSession> hotspot,
        IReadOnlyList<UmSessionRow> manager)
    {
        var local=Hotspot(hotspot);
        var radius=UserManager(manager);
        if(view=="hotspot")return local;
        if(view=="userman")return radius;
        if(view!="all")throw new ArgumentOutOfRangeException(nameof(view));

        // Same account and nonempty IP means a single online session seen
        // from both /ip/hotspot/active and User Manager accounting. A user
        // alone, or an unknown address, is not evidence of identical sessions.
        var combined=local.ToList();
        var usedHotspot=new HashSet<int>();
        foreach(var um in radius)
        {
            var match=-1;
            if(um.Address!="غير متاح")
            {
                for(int i=0;i<local.Count;i++)
                {
                    if(usedHotspot.Contains(i))continue;
                    if(!string.Equals(local[i].User,um.User,
                        StringComparison.OrdinalIgnoreCase))continue;
                    if(!string.Equals(local[i].Address,um.Address,
                        StringComparison.OrdinalIgnoreCase))continue;
                    match=i;break;
                }
            }
            if(match>=0)
            {
                usedHotspot.Add(match);
                combined[match]=combined[match] with
                {
                    Source="الهوتسبوت + اليوزر مانجر",
                    MacAddress=local[match].MacAddress=="غير متاح"
                        ?um.MacAddress:local[match].MacAddress,
                    Status="متصل (المصدران)"
                };
            }
            else combined.Add(um);
        }
        return combined;
    }
}
