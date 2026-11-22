using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Services;

/// <summary>Only adopts a batch after complete cross-service name checks on its pinned router.</summary>
public sealed class VoucherBatchPreparer(RouterGateway gateway,VoucherStore store)
{
    public async Task<IReadOnlyList<Voucher>> PrepareAsync(VoucherGenerationOptions options,string service,string customer,
        Func<IReadOnlyList<Voucher>,VoucherBatchPrintSnapshot?>? printSnapshot=null,
        CancellationToken ct=default,IProgress<string>? progress=null)
    {
        if(service is not ("hotspot" or "userman" or "pppoe"))throw new ArgumentException("خدمة الكرت غير صالحة.");
        if(service=="userman"&&string.IsNullOrWhiteSpace(customer))throw new ArgumentException("حساب المدير مطلوب.");
        using var operation=new VoucherPipelineOperation();
        var router=await gateway.ReadRouterIdentityAsync(ct);
        progress?.Report("فحص الأسماء في خدمات الراوتر قبل اعتماد الدفعة...");
        var names=await gateway.ReadExistingVoucherNamesAsync(ct,requiredService:service);
        return await Task.Run(async()=>
        {
            ct.ThrowIfCancellationRequested();
            var reserved=new HashSet<string>(store.ReservedNamesAcrossServices(router),StringComparer.OrdinalIgnoreCase);
            reserved.UnionWith(names);
            progress?.Report("توليد أرقام الكروت المتاحة...");
            var cards=VoucherGenerator.Generate(options,reserved)
                .Select(c=>c with{Service=service,Customer=customer,RouterId=router}).ToArray();
            var snapshot=printSnapshot?.Invoke(cards);
            await gateway.VerifyRouterIdentityAsync(router,ct);
            ct.ThrowIfCancellationRequested();
            progress?.Report("حفظ الكروت في قاعدة البيانات المحلية...");
            // Rechecks local conflicts inside the SAME transaction as adoption.
            // Sending still makes a fresh router check: external changes cannot
            // be locked across RouterOS and SQLite.
            store.SavePreparedBatch(cards,snapshot);
            return (IReadOnlyList<Voucher>)cards;
        },ct);
    }
}
