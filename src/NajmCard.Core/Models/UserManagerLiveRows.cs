namespace NajmCard.Core.Models;

/// <summary>Router-originated rows. Missing values stay unknown; no demo data.</summary>
public sealed record UmDataSnapshot<T>(RouterOsGeneration Generation,
    IReadOnlyList<T> Rows)
{
    // Distinguish reliable profile rows from incomplete legacy references.
    // Consumers can still use verified accounts/sessions independently.
    public bool IsComplete { get; init; }=true;
    public string Diagnostic { get; init; }="";
}

public sealed record UmAccountRow(
    string Id,string Name,string Profile,string Status,bool? Disabled,
    string EndTime,long? DownloadBytes,long? UploadBytes,long? TransferLimit,
    int? ActiveSessions,string LastSeen,string Diagnostic)
{
    public long? UsedBytes =>
        DownloadBytes is long down && UploadBytes is long up
            ? (down>long.MaxValue-up ? long.MaxValue : down+up) : null;
    public string Remaining =>
        TransferLimit is long limit && limit>0 && UsedBytes is long used
            ? Math.Max(0,limit-used).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "غير متاح";
}

public sealed record UmPlanRow(
    string Id,string Kind,string Name,string Price,string Validity,
    string StartsWhen,string TransferLimit,string DownloadLimit,
    string UploadLimit,string UptimeLimit,string DownloadRate,
    string UploadRate,string AssociatedLimits)
{
    public string KindLabel => Kind=="Profile"?"باقة":"قيد";
}

public sealed record UmSessionRow(
    string Id,string Name,string Started,string Ended,string Uptime,
    long? DownloadBytes,long? UploadBytes,string Address,string Mac,
    bool? IsActive,DateTime? StartedAt,string RawStatus)
{
    public long? TotalBytes =>
        DownloadBytes is long down && UploadBytes is long up
            ? (down>long.MaxValue-up ? long.MaxValue : down+up) : null;
    public string Status => IsActive==true?"نشطة":
        IsActive==false?"منتهية":"غير معروف";
}
