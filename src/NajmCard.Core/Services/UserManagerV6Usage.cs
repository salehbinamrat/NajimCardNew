using System.Globalization;
using System.Text.RegularExpressions;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Strict RouterOS 6 usage classification. The only proof of expiry is an
/// exhausted strictly positive TOTAL Transfer Limit, never "Total Time Left".
/// Mirrors Hadramout core/network.py::_userman_lifecycle_v6.
/// </summary>
public static class UserManagerV6Usage
{
    private static string Get(IReadOnlyDictionary<string,string> row, string key) =>
        row.TryGetValue(key,out var result) ? result : "";

    public static long ParseBytes(string? value)
    {
        var raw = (value??"").Trim().Replace(" ","").ToLowerInvariant();
        if (raw.Length==0 || raw.Length>128 || raw is "unlimited" or "none")
            return 0;
        if (long.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out var rawBytes))
            return Math.Max(0,rawBytes);
        var match=Regex.Match(raw,@"^([0-9]+(?:\.[0-9]+)?)([kmgt]?i?b?)$",
            RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse(match.Groups[1].Value,
                NumberStyles.Float,CultureInfo.InvariantCulture,out var amount))
            return 0;
        var unit=match.Groups[2].Value;
        var power=unit.Length==0 ? 0 : unit[0] switch
        {
            'k'=>1,'m'=>2,'g'=>3,'t'=>4,'b'=>0,_=>-1
        };
        if (power<0) return 0;
        var bytes=amount*Math.Pow(1024,power);
        return double.IsFinite(bytes)&&bytes>=0&&bytes<long.MaxValue
            ? (long)bytes : 0;
    }

    private static bool Truth(string value) =>
        value.Equals("true",StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes",StringComparison.OrdinalIgnoreCase)
        || value=="1";

    internal static bool TryBoolean(string value,out bool result)
    {
        var text=value.Trim().ToLowerInvariant();
        result=text is "true" or "yes" or "1";
        return result||text is "false" or "no" or "0";
    }

    // One strict parser is shared by validation and the actual calculation.
    // Decimal avoids rounding the Int64 boundary through a double conversion.
    internal static bool TryBytes(string value,out long bytes)
    {
        bytes=0;
        var raw=value.Trim().Replace(" ","").ToLowerInvariant();
        if(raw.Length is 0 or >128)return false;
        var match=Regex.Match(raw,@"^([0-9]+(?:\.[0-9]+)?)([kmgt]?i?b?)$",
            RegexOptions.CultureInvariant);
        if(!match.Success||!decimal.TryParse(match.Groups[1].Value,
            NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount))return false;
        var unit=match.Groups[2].Value;
        var power=unit.Length==0?0:unit[0] switch
        {'k'=>1,'m'=>2,'g'=>3,'t'=>4,'b'=>0,_=>-1};
        if(power<0)return false;
        decimal factor=1;for(int i=0;i<power;i++)factor*=1024;
        if(amount>long.MaxValue/factor)return false;
        bytes=(long)(amount*factor);
        return true;
    }

    internal static bool TryParseQuota(string value,out long bytes)
    {
        bytes=0;
        return value.Trim().ToLowerInvariant() is "unlimited" or "none"||TryBytes(value,out bytes);
    }

    internal static bool TryCounters(IReadOnlyDictionary<string,string> row,
        out long download,out long upload)
    {
        // Real v6 legacy counters remain authoritative when BOTH are valid.
        // Otherwise use a complete validated total-* pair, never mixed fields.
        if(TryBytes(Get(row,"download-used"),out download)&&
            TryBytes(Get(row,"upload-used"),out upload))return true;
        if(TryBytes(Get(row,"total-download-used"),out download)&&
            TryBytes(Get(row,"total-upload-used"),out upload))return true;
        download=upload=0;return false;
    }

    internal static bool TryAdministrativeState(IReadOnlyDictionary<string,string> row,
        out bool disabled,out bool incomplete)
    {
        bool disabledKnown=TryBoolean(Get(row,"disabled"),out disabled);
        bool incompleteKnown=TryBoolean(Get(row,"incomplete"),out incomplete);
        return disabledKnown&&incompleteKnown;
    }

    private static bool HasPositiveNumber(string value)
    {
        if (value.Length>128) return false;
        foreach (Match match in Regex.Matches(value,@"\d+(?:\.\d+)?"))
            if (double.TryParse(match.Value,NumberStyles.Float,
                CultureInfo.InvariantCulture,out var n) && n>0)
                return true;
        return false;
    }

    public static UserManagerV6Voucher Classify(
        IReadOnlyDictionary<string,string> row, long profileTransferLimit)
        // A bare number is not evidence of profile ownership or resolved links.
        // Keep this API for callers that inspect raw details, never deletion.
        =>Classify(row,new UserManagerV6ResolvedQuota("","",Math.Max(0,profileTransferLimit),
            false,"لم تُتحقق ملكية الباقة وحدودها من تعريفات RouterOS."));

    internal static UserManagerV6Voucher Classify(
        IReadOnlyDictionary<string,string> row,UserManagerV6ResolvedQuota quota)
    {
        ArgumentNullException.ThrowIfNull(row);
        var id=Get(row,".id");
        var username=Get(row,"username");
        if (string.IsNullOrEmpty(username)) username=Get(row,"name");
        var profile=Get(row,"actual-profile");
        bool usageVerified=TryCounters(row,out long down,out long up);
        long directLimit=ParseBytes(Get(row,"transfer-limit"));
        long transfer=quota.IsVerified?quota.Bytes:
            directLimit>0?directLimit:quota.Bytes;
        var consumed=down>long.MaxValue-up?long.MaxValue:down+up;
        bool administrativeVerified=TryAdministrativeState(row,out var disabled,out var incomplete);
        var lastSeen=Get(row,"last-seen").Trim();
        var started=consumed>0 || HasPositiveNumber(Get(row,"money-used"))
            || HasPositiveNumber(Get(row,"uptime-used"))
            || Truth(Get(row,"active"))
            || HasPositiveNumber(Get(row,"active-sessions"))
            || !string.IsNullOrWhiteSpace(Get(row,"last-used-ip"))
            || !string.IsNullOrWhiteSpace(Get(row,"last-used-mac"))
            || (lastSeen.Length>0 && !lastSeen.Equals("never",StringComparison.OrdinalIgnoreCase)
                && !lastSeen.Equals("unknown",StringComparison.OrdinalIgnoreCase));
        string status,reason,source="";
        if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(username)
            ||!usageVerified||!administrativeVerified||!quota.IsVerified)
        {
            status="unclassified";
            reason="بيانات غير مؤكدة؛ لا يُعتمد التصنيف أو الحذف. "+quota.Diagnostic;
        }
        else if (disabled) { status="disabled";reason="معطل إدارياً"; }
        else if (incomplete) { status="incomplete";reason="حساب غير مكتمل"; }
        else if (started && transfer>0 && consumed>=transfer)
        {
            status="expired";
            reason="استنفد حد إجمالي نقل البيانات";
            source="transfer-limit";
        }
        else if (started && profile.Length>0) { status="active";reason="بدأ الاستخدام"; }
        else if (!started && profile.Length>0) { status="valid";reason="لم يبدأ الاستخدام"; }
        else { status="unclassified";reason="لا يوجد دليل كافٍ على حالة الكرت"; }
        var rawSessions=Get(row,"active-sessions");
        bool verifiedSessions=row.ContainsKey("active-sessions") &&
            int.TryParse(rawSessions,NumberStyles.Integer,CultureInfo.InvariantCulture,out _)
            && !rawSessions.TrimStart().StartsWith('-');
        int.TryParse(rawSessions,NumberStyles.Integer,CultureInfo.InvariantCulture,out int sessions);
        return new UserManagerV6Voucher(id,username,profile,status,reason,source,
            down,up,transfer,started,disabled,incomplete,
            Get(row,"uptime-used"),Get(row,"last-seen"),
            Math.Clamp(sessions,0,1000000),verifiedSessions)
        {
            Customer=quota.Owner,ProfileId=quota.ProfileId,
            UsageVerified=usageVerified,AdministrativeStateVerified=administrativeVerified,
            QuotaVerified=quota.IsVerified
        };
    }
}
