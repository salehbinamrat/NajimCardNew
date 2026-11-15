using System.IO;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Services;

/// <summary>Explicit read-only reconciliation; only proven-absent rows can resume.</summary>
public sealed class VoucherBatchRecovery(RouterGateway gateway,VoucherStore store)
{
    public Task<IReadOnlyList<Voucher>> ReviewAsync(string routerId,string service,string batchId,
        CancellationToken ct=default)=>Task.Run(async ()=>
    {
        using var batchLock=store.LockBatch(routerId,service,batchId);
        await gateway.VerifyRouterIdentityAsync(routerId,ct);
        var rows=store.GetBatchVouchers(service,batchId,routerId);
        foreach(var card in rows.Where(x=>x.DeliveryStatus!="SENT"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var evidence=await gateway.ReconcileVoucherAsync(card,ct);
                store.ApplyReconciliation(card,evidence);
            }
            catch(RouterIdentityMismatchException){throw;}
            catch(Exception ex) when(ex is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException)
            {
                store.ApplyReconciliation(card,new("UNKNOWN","تعذرت قراءة الراوتر بالكامل؛ لم يُثبت غياب الكرت ولم يُرسل شيء."));
                break;
            }
        }
        return store.GetBatchVouchers(service,batchId,routerId);
    },ct);

    public async Task<BatchDeliveryResult> ResumeAsync(string routerId,string service,string batchId,
        CancellationToken ct=default,IProgress<string>? progress=null)
    {
        // Dispatcher independently verifies identity, collisions and the durable
        // state again under the same cross-process batch lock. A stale UI row
        // can never unlock SENDING/UNKNOWN or reissue a completed card.
        var ready=store.GetBatchVouchers(service,batchId,routerId).Where(x=>x.DeliveryStatus=="READY").ToArray();
        if(ready.Length==0)throw new InvalidOperationException("لا توجد كروت ثبت غيابها؛ نفذ المطابقة أولا.");
        return await new VoucherBatchDispatcher(gateway,store).SendAsync(ready,ct,progress);
    }
}
