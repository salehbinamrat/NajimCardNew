using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    /// <summary>
    /// Reads the active Hotspot connections ONCE. The Radius flag (RouterOS
    /// HotSpot 'R' flag) is an authentication-source distinction, not a UM
    /// accounting session. Both groups are subsets of the same live read,
    /// preventing duplicates in the all-connections tab.
    /// </summary>
    public async Task<IReadOnlyList<LiveHotspotConnection>> ReadLiveHotspotConnectionsAsync(
        CancellationToken ct=default)
    {
        // Full read is intentional: some RouterOS 6 builds omit the 'radius'
        // flag from a projected print. No passwords or user secrets are read.
        var rows=await client.CommandAsync("/ip/hotspot/active/print",
            cancellationToken:ct);
        if(rows.Any(row=>string.IsNullOrWhiteSpace(Get(row,"user"))))
            throw new InvalidDataException(
                "لم يرجع RouterOS اسم المستخدم لكل اتصال نشط؛ لا يمكن اعتماد العدد.");
        return rows.Select(row=>new LiveHotspotConnection(
            Get(row,".id"),Get(row,"user"),
            Get(row,"address"),Get(row,"mac-address"),Get(row,"uptime"),
            Get(row,"server"),Get(row,"login-by"),SourceOf(row))).ToArray();
    }

    private static HotspotConnectionOrigin SourceOf(
        IReadOnlyDictionary<string,string> row)
    {
        if(row.TryGetValue("radius",out var raw))
        {
            if(raw.Equals("yes",StringComparison.OrdinalIgnoreCase)||
                raw.Equals("true",StringComparison.OrdinalIgnoreCase)||
                raw=="1")return HotspotConnectionOrigin.UserManagerRadius;
            if(raw.Equals("no",StringComparison.OrdinalIgnoreCase)||
                raw.Equals("false",StringComparison.OrdinalIgnoreCase)||
                raw=="0")return HotspotConnectionOrigin.LocalHotspot;
            // Nonstandard values should never be silently counted as local.
            return HotspotConnectionOrigin.Unknown;
        }
        // RouterOS 6 sometimes returns the same flag via the 'flags' column.
        if(row.TryGetValue("flags",out var flags))
            return flags.Split(' ',StringSplitOptions.RemoveEmptyEntries)
                .Any(value=>value.Contains('R'))
                ?HotspotConnectionOrigin.UserManagerRadius
                :HotspotConnectionOrigin.LocalHotspot;
        // RouterOS API's boolean flags are presence-only: an omitted radius
        // flag on a complete /active/print record is a local Hotspot login.
        return HotspotConnectionOrigin.LocalHotspot;
    }
}
