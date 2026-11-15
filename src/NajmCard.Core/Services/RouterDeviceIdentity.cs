using System.Security.Cryptography;
using System.Text;

namespace NajmCard.Core.Services;

/// <summary>Device evidence only: never an address, login name or mutable system identity.</summary>
public static class RouterDeviceIdentity
{
    private static string? Fingerprint(IReadOnlyList<IReadOnlyDictionary<string,string>> rows,
        string key,string kind)
    {
        if(rows.Count!=1||!rows[0].TryGetValue(key,out var value))return null;
        value=value.Trim();
        if(value.Length is <3 or >128 || value.Any(char.IsControl) ||
            value.ToLowerInvariant() is "unknown" or "none" or "n/a" ||
            value.All(c=>c=='0'||c=='-'))return null;
        return kind+":"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public static async Task<string> ReadAsync(IRouterCommandClient client,CancellationToken ct=default)
    {
        // RouterBOARD serial is stable across endpoint/name changes. CHR has a
        // system-id; x86 installations expose software-id instead. A source
        // change intentionally refuses old batches rather than silently rebinding.
        IReadOnlyList<IReadOnlyDictionary<string,string>> board=[];
        try{board=await client.CommandAsync("/system/routerboard/print",cancellationToken:ct);}
        catch(RouterOsTrapException){ /* CHR/x86 may not expose routerboard. */ }
        var serial=Fingerprint(board,"serial-number","rb");
        if(serial is not null)return serial;
        var license=await client.CommandAsync("/system/license/print",cancellationToken:ct);
        return Fingerprint(license,"system-id","chr")??Fingerprint(license,"software-id","sw")
            ??throw new InvalidOperationException("تعذر إثبات هوية ثابتة للراوتر؛ أُوقف إرسال الدفعات ومطابقتها.");
    }
}

public sealed class RouterIdentityMismatchException() : InvalidOperationException(
    "هوية الراوتر لا تطابق الراوتر المحفوظ للدفعة؛ لم تُرسل أو تُطابق الكروت.");
