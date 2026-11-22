namespace NajmCard.Core.Services;

/// <summary>
/// RouterOS 6 can legitimately take minutes to enumerate tens of thousands of
/// User Manager accounts. Do not apply its slow-read allowance to writes.
/// </summary>
public sealed record RouterOsCommandTimeoutProfile(
    TimeSpan Total, TimeSpan Idle, int MaxRows, long MaxResponseBytes);

public static class RouterOsCommandTimeoutPolicy
{
    public static RouterOsCommandTimeoutProfile For(string command,bool bounded=false)
    {
        bool largeInventory = !bounded && command is
            "/tool/user-manager/user/print" or "/user-manager/user/print"
            or "/tool/user-manager/session/print" or "/user-manager/session/print";
        return largeInventory
            ? new RouterOsCommandTimeoutProfile(
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2),
                250000, 128L * 1024 * 1024)
            : new RouterOsCommandTimeoutProfile(
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20),
                30000, 128L * 1024 * 1024);
    }

    public static RouterOsCommandTimeoutProfile ForRequest(IReadOnlyList<string> words)
    {
        if(words.Count==0)throw new ArgumentException("Empty request",nameof(words));
        // Session account filters can still return a large history. Only counts
        // and single account/ID queries receive the short request deadline.
        bool bounded=words.Any(w=>w.StartsWith("=count-only=",StringComparison.Ordinal)
            ||w.StartsWith("?.id=",StringComparison.Ordinal)
            ||(w.StartsWith("?name=",StringComparison.Ordinal)||w.StartsWith("?username=",StringComparison.Ordinal))
                &&!words[0].EndsWith("/session/print",StringComparison.Ordinal));
        return For(words[0],bounded);
    }
}
