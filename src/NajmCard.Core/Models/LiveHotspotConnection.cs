namespace NajmCard.Core.Models;

/// <summary>
/// An authenticated Hotspot connection reported by RouterOS right now.
/// A RADIUS flag identifies the authentication channel, not the historical
/// User Manager session count or a particular external RADIUS provider.
/// </summary>
public enum HotspotConnectionOrigin
{
    UserManagerRadius,
    LocalHotspot,
    Unknown
}

public sealed record LiveHotspotConnection(
    string Id,string User,string Address,string MacAddress,string Uptime,
    string Server,string LoginBy,HotspotConnectionOrigin Origin)
{
    public string OriginLabel=>Origin switch
    {
        HotspotConnectionOrigin.UserManagerRadius=>"User Manager / RADIUS",
        HotspotConnectionOrigin.LocalHotspot=>"Hotspot",
        _=>"غير محدد"
    };
}
