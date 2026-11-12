using System.Globalization;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Read only. Paid profile prices do not prove revenue: only 'approved'
/// payment rows from RouterOS v7 are counted. Local voucher sales require
/// a separate trusted accounting system, and are not invented here.
/// </summary>
public sealed partial class RouterGateway
{
    public async Task<UmDataSnapshot<UmApprovedPayment>> ReadUmApprovedPaymentsAsync(
        CancellationToken ct=default)
    {
        var generation=await ReadRouterOsGenerationAsync(ct);
        if(generation!=RouterOsGeneration.V7)
            throw new NotSupportedException(
                "تقرير المدفوعات المؤكدة بهذا المسار متاح لـ RouterOS 7 فقط.");
        var rows=await client.CommandAsync("/user-manager/payment/print",
            new Dictionary<string,string>
            {
                [".proplist"]=".id,price,currency,trans-status,trans-end,trans-start,profile"
            },ct);
        if(rows.Any(x=>!x.ContainsKey(".id")||!x.ContainsKey("trans-status")))
            throw new InvalidDataException("سجلات المدفوعات لا تحتوي الحالة أو المعرف.");
        var confirmed=new List<UmApprovedPayment>();
        foreach(var row in rows)
        {
            if(!string.Equals(UmValue(row,"trans-status"),"approved",
                StringComparison.OrdinalIgnoreCase))continue;
            if(!decimal.TryParse(UmValue(row,"price"),
                NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount)
                || amount<0)
                throw new InvalidDataException(
                    "تم العثور على دفعة موافق عليها بقيمة غير صالحة.");
            var date=ParseUmDate(UmValue(row,"trans-end")) ??
                ParseUmDate(UmValue(row,"trans-start"));
            confirmed.Add(new UmApprovedPayment(
                UmValue(row,".id"),amount,UmKnown(row,"currency"),
                date,UmKnown(row,"profile")));
        }
        return new UmDataSnapshot<UmApprovedPayment>(generation,confirmed);
    }

    public async Task<IReadOnlyList<UmRouterBackup>> ReadUmRouterBackupsAsync(
        CancellationToken ct=default)
    {
        // RouterOS v7 .umb backup files are visible in the router Files menu.
        // Listing is safe; no file downloads or database restores are performed.
        var records=await client.CommandAsync("/file/print",
            new Dictionary<string,string>
            {
                [".proplist"]=".id,name,size,creation-time"
            },ct);
        if(records.Any(x=>!x.ContainsKey(".id")||!x.ContainsKey("name")))
            throw new InvalidDataException(
                "لم تُقرأ أسماء ملفات النسخ من الراوتر بشكل موثوق.");
        return records.Where(row=>UmValue(row,"name").EndsWith(".umb",
            StringComparison.OrdinalIgnoreCase))
            .Select(row=>new UmRouterBackup(UmValue(row,".id"),
                UmValue(row,"name"),UmKnown(row,"size"),
                UmKnown(row,"creation-time"))).ToArray();
    }
}
