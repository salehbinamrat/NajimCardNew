namespace NajmCard.Core.Models;

/// <summary>
/// A single RouterOS v6 User Manager row. Counts are for voucher usage lifecycle,
/// not current session connections. No credentials are copied to this model.
/// </summary>
public sealed record UserManagerV6Voucher(
    string Id, string Username, string Profile, string Status, string Reason,
    string ExpirySource, long DownloadBytes, long UploadBytes,
    long TransferLimit, bool Started, bool Disabled, bool Incomplete,
    string UptimeUsed="",string LastSeen="",int ActiveSessions=0,
    bool ActiveSessionsVerified=false)
{
    public string Customer { get; init; }="";
    public string ProfileId { get; init; }="";
    public bool UsageVerified { get; init; }
    public bool AdministrativeStateVerified { get; init; }
    public bool QuotaVerified { get; init; }
    public bool IsVerified=>UsageVerified&&AdministrativeStateVerified&&QuotaVerified
        &&Id.Length>0&&Username.Length>0&&Customer.Length>0&&ProfileId.Length>0;
    public bool CanDeleteAsTransferExpired=>IsVerified&&Status=="expired"
        &&ExpirySource=="transfer-limit"&&Started&&!Disabled&&!Incomplete
        &&TransferLimit>0&&ConsumedBytes>=TransferLimit;
    public long ConsumedBytes => DownloadBytes > long.MaxValue - UploadBytes
        ? long.MaxValue : DownloadBytes + UploadBytes;
    public long RemainingBytes => TransferLimit<=0 ? 0 :
        Math.Max(0,TransferLimit-ConsumedBytes);
    public string RemainingLabel => !QuotaVerified||!UsageVerified ? "غير مؤكد" :
        TransferLimit<=0 ? "غير محدد" : RemainingBytes.ToString();
    public string TransferLimitLabel=>QuotaVerified?TransferLimit.ToString():"غير مؤكد";
    public string ConsumedBytesLabel=>UsageVerified?ConsumedBytes.ToString():"غير متاح";
    public string DownloadBytesLabel=>UsageVerified?DownloadBytes.ToString():"غير متاح";
    public string UploadBytesLabel=>UsageVerified?UploadBytes.ToString():"غير متاح";
    public string ActiveSessionsLabel=>ActiveSessionsVerified?ActiveSessions.ToString():"غير متاح";
}

public sealed record UserManagerV6Inventory(
    IReadOnlyList<UserManagerV6Voucher> Users, bool IsComplete, string Diagnostic)
{
    public DateTimeOffset ReadAt { get; init; }=DateTimeOffset.UtcNow;
    public bool IsFromCache { get; init; }
    public long Revision { get; init; }
    public string ReadLabel=>(IsFromCache?"بيانات مخزنة؛ وقت القراءة: ":"وقت القراءة من الراوتر: ")+
        ReadAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public int Total => Users.Count;
    public int Valid => Users.Count(x=>x.Status=="valid");
    public int Active => Users.Count(x=>x.Status=="active");
    public int Expired => Users.Count(x=>x.Status=="expired");
    public int Disabled => Users.Count(x=>x.Status=="disabled");
    public int Incomplete => Users.Count(x=>x.Status=="incomplete");
    public int Unclassified => Users.Count(x=>x.Status=="unclassified");
}
