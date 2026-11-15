using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

public sealed record VoucherDeliveryEvent(long Id,string Username,string PreviousState,
    string State,string Timestamp,string AttemptId,string Reason,string RouterRecordId);

public sealed partial class VoucherStore
{
    private static string ResolveBatchRouterId(SqliteConnection db,string service,string batchId)
    {
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT DISTINCT router_id FROM vouchers WHERE service=$service AND batch_id=$batch LIMIT 2";
        cmd.Parameters.AddWithValue("$service",service);cmd.Parameters.AddWithValue("$batch",batchId);
        using var reader=cmd.ExecuteReader();
        var id=reader.Read()?reader.GetString(0):"";
        if(reader.Read())throw new InvalidOperationException("اسم الدفعة موجود على أكثر من راوتر؛ حدد هويته صراحة.");
        return id;
    }

    private static void RequireRouter(string routerId)
    {
        if(string.IsNullOrWhiteSpace(routerId))throw new InvalidOperationException(
            "ملكية الراوتر غير معروفة لهذه الدفعة القديمة؛ لا يمكن إرسالها أو مطابقتها تلقائيا.");
    }

    public IDisposable LockBatch(string routerId,string service,string batchId)
    {
        RequireRouter(routerId);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new[]{routerId,service,batchId}))));
        var folder=Path.Combine(DirectoryPath,"BatchLocks");Directory.CreateDirectory(folder);
        try{return new FileStream(Path.Combine(folder,key+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException ex){throw new InvalidOperationException("الدفعة قيد الإرسال أو المطابقة في عملية أخرى؛ انتظر اكتمالها.",ex);}
    }

    private static void WriteDeliveryEvent(SqliteConnection db,SqliteTransaction tx,Voucher card,
        string previous,string state,string reason)
    {
        using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO voucher_delivery_events(router_id,service,batch_id,username,previous_state,
                state,at_utc,attempt_id,reason,router_record_id)
            VALUES($router,$service,$batch,$user,$previous,$state,$now,$attempt,$reason,$remote)
            """;
        cmd.Parameters.AddWithValue("$router",card.RouterId);cmd.Parameters.AddWithValue("$service",card.Service);
        cmd.Parameters.AddWithValue("$batch",card.BatchId);cmd.Parameters.AddWithValue("$user",card.Username);
        cmd.Parameters.AddWithValue("$previous",previous);cmd.Parameters.AddWithValue("$state",state);
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$attempt",card.AttemptId);
        cmd.Parameters.AddWithValue("$reason",reason);cmd.Parameters.AddWithValue("$remote",card.RouterRecordId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<VoucherDeliveryEvent> ReadDeliveryEvents(string routerId,string service,string batchId)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT id,username,previous_state,state,at_utc,attempt_id,reason,router_record_id
            FROM voucher_delivery_events WHERE router_id=$router AND service=$service AND batch_id=$batch ORDER BY id
            """;
        cmd.Parameters.AddWithValue("$router",routerId);cmd.Parameters.AddWithValue("$service",service);
        cmd.Parameters.AddWithValue("$batch",batchId);
        using var reader=cmd.ExecuteReader();var result=new List<VoucherDeliveryEvent>();
        while(reader.Read())result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),
            reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7)));
        return result;
    }

    internal void ApplyReconciliation(Voucher observed,VoucherReconciliation evidence)
    {
        RequireRouter(observed.RouterId);
        if(evidence.State is not ("READY" or "SENT" or "UNKNOWN" or "CONFLICT"))throw new ArgumentException("نتيجة مطابقة غير صالحة.");
        using var db=Open();using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
            UPDATE vouchers SET delivery_status=$state,sent=CASE WHEN $state='SENT' THEN 1 ELSE 0 END,
                updated_utc=$now,last_error=$reason,router_record_id=$remote
            WHERE router_id=$router AND service=$service AND batch_id=$batch AND username=$user
                AND delivery_status=$previous AND delivery_status<>'SENT' AND attempt_id=$attempt AND updated_utc=$observed
            """;
        cmd.Parameters.AddWithValue("$state",evidence.State);cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$reason",evidence.Reason);cmd.Parameters.AddWithValue("$remote",evidence.RouterRecordId);
        cmd.Parameters.AddWithValue("$router",observed.RouterId);cmd.Parameters.AddWithValue("$service",observed.Service);
        cmd.Parameters.AddWithValue("$batch",observed.BatchId);cmd.Parameters.AddWithValue("$user",observed.Username);
        cmd.Parameters.AddWithValue("$previous",observed.DeliveryStatus);cmd.Parameters.AddWithValue("$attempt",observed.AttemptId);
        cmd.Parameters.AddWithValue("$observed",observed.UpdatedUtc);
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("تغير سجل الكرت أثناء المطابقة؛ لم يُعتمد رد قديم.");
        WriteDeliveryEvent(db,tx,observed with {RouterRecordId=evidence.RouterRecordId},observed.DeliveryStatus,
            evidence.State,evidence.Reason);tx.Commit();
    }
}
