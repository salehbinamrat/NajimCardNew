using System.Globalization;

namespace NajmCard.Desktop.Services;

public sealed record VoucherDeliveryBatch(string Service, string BatchId, string Profile,
    DateTimeOffset CreatedAt, int Total, int Sent, int Pending, int Failed, int NeedsReview)
{
    public string RouterId { get; init; }="";
    public string RouterLabel=>RouterId.Length==0?"ملكية الراوتر غير معروفة":RouterId[..Math.Min(18,RouterId.Length)];
    public string ServiceLabel => Service switch
    {
        "hotspot" => "Hotspot",
        "userman" => "User Manager",
        "pppoe" => "PPPoE",
        _ => Service
    };

    public string DisplayName =>
        $"{Profile} · {ServiceLabel} · {CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {BatchId} · {RouterLabel}";
}

public sealed record VoucherDeliveryEntry(string Number, string State)
{
    public string UpdatedUtc { get; init; }="";
    public string Result { get; init; }="";
    public string AttemptId { get; init; }="";
    public string Status => State switch
    {
        "SENT" => "مؤكد الإرسال",
        "CREATED" => "لم يرسل",
        "FAILED" => "رفض الإرسال",
        "SENDING" => "بدأ الإرسال؛ يلزم التحقق",
        "UNKNOWN" => "نتيجة الإرسال مجهولة",
        "READY" => "ثبت غيابه؛ قابل للاستئناف",
        "CONFLICT" => "تعارض؛ فحص يدوي",
        _ => "يحتاج مراجعة"
    };
}

public sealed partial class VoucherStore
{
    public IReadOnlyList<VoucherDeliveryBatch> ListDeliveryBatches()
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            WITH recent AS (
                SELECT router_id,service, batch_id, MIN(profile) AS profile, MAX(created_at) AS created_at,
                    COUNT(*) AS total,
                    SUM(CASE WHEN sent=1 AND delivery_status='SENT' THEN 1 ELSE 0 END) AS confirmed,
                    SUM(CASE WHEN sent=0 AND delivery_status IN ('CREATED','READY') THEN 1 ELSE 0 END) AS pending,
                    SUM(CASE WHEN sent=0 AND delivery_status='FAILED' THEN 1 ELSE 0 END) AS failed
                FROM vouchers
                WHERE batch_id <> ''
                GROUP BY router_id,service,batch_id
                ORDER BY created_at DESC,service,batch_id
            )
            SELECT service,batch_id,profile,created_at,total,confirmed,pending,failed,router_id
            FROM recent ORDER BY created_at DESC,service,batch_id
            """;
        using var reader = command.ExecuteReader();
        var result = new List<VoucherDeliveryBatch>();
        while (reader.Read())
        {
            int total = reader.GetInt32(4), sent = reader.GetInt32(5);
            int pending = reader.GetInt32(6), failed = reader.GetInt32(7);
            result.Add(new VoucherDeliveryBatch(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                total, sent, pending, failed, total - sent - pending - failed) {RouterId=reader.GetString(8)});
        }
        return result;
    }

    public IReadOnlyList<VoucherDeliveryEntry> ReadDeliveryEntries(string service, string batchId,string? routerId=null)
    {
        using var db = Open();
        routerId??=ResolveBatchRouterId(db,service,batchId);
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT username,delivery_status,updated_utc,last_error,attempt_id
            FROM vouchers
            WHERE service=$service AND batch_id=$batch AND router_id=$router
            ORDER BY created_at,username
            """;
        command.Parameters.AddWithValue("$router",routerId);
        command.Parameters.AddWithValue("$service", service);
        command.Parameters.AddWithValue("$batch", batchId);
        using var reader = command.ExecuteReader();
        var result = new List<VoucherDeliveryEntry>();
        while (reader.Read())
            result.Add(new VoucherDeliveryEntry(reader.GetString(0), reader.GetString(1))
            {UpdatedUtc=reader.GetString(2),Result=reader.GetString(3),AttemptId=reader.GetString(4)});
        return result;
    }
    /// <summary>
    /// Read the acknowledged RouterOS failure reason from durable SQLite state.
    /// Never infer a successful send from this diagnostic message.
    /// </summary>
    public string? FirstDeliveryError(string service,string batchId,string? routerId=null)
    {
        if(service is not ("hotspot" or "userman" or "pppoe") ||
            string.IsNullOrWhiteSpace(batchId))
            throw new ArgumentException("معرف الدفعة أو الخدمة غير صالح.");
        using var db=Open();
        routerId??=ResolveBatchRouterId(db,service,batchId);
        using var command=db.CreateCommand();
        command.CommandText="""
            SELECT last_error FROM vouchers
            WHERE service=$service AND batch_id=$batch AND router_id=$router
                  AND last_error IS NOT NULL AND last_error <> ''
            ORDER BY created_at,username LIMIT 1
            """;
        command.Parameters.AddWithValue("$router",routerId);
        command.Parameters.AddWithValue("$service",service);
        command.Parameters.AddWithValue("$batch",batchId);
        return command.ExecuteScalar() as string;
    }

}
