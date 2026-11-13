using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>Outcome of verified single-ID session history removal.</summary>
public sealed record UmSessionRemovalResult(int Removed,int Requested,bool Uncertain,string Diagnostic);

public sealed partial class RouterGateway
{
    /// <summary>
    /// Delete explicitly selected ended RADIUS accounting records only.
    /// Never clears a whole database, touches active sessions or retries an
    /// ambiguous RouterOS reply. Rechecks each exact session ID immediately
    /// before and after the write to defend against stale UI snapshots.
    /// </summary>
    public async Task<UmSessionRemovalResult> RemoveUmSessionsAsync(
        RouterOsGeneration generation,IReadOnlyList<UmSessionRow> selected,
        CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if(selected.Count is < 1 or > 100)
            throw new ArgumentException("حدد من جلسة واحدة إلى 100 جلسة في العملية.");
        if(selected.Any(row=>row.IsActive!=false ||
            string.IsNullOrWhiteSpace(row.Name)) ||
            selected.Select(row=>row.Id).Distinct(StringComparer.Ordinal).Count()!=selected.Count)
            throw new InvalidOperationException(
                "الاختيار يحتوي جلسات نشطة أو مجهولة الحالة أو معرفات متكررة.");
        foreach(var row in selected)ValidateInternalId(row.Id);

        if(client is not IRouterQueryClient query)
            throw new NotSupportedException(
                "لا يمكن الحذف دون قراءة الجلسات مباشرة بالمعرف من الراوتر.");
        var actual=await ReadRouterOsGenerationAsync(ct);
        if(actual!=generation)
            throw new InvalidOperationException("تغير إصدار RouterOS. حدّث الجلسات قبل الحذف.");

        var parent=generation==RouterOsGeneration.V6
            ?"/tool/user-manager/session":"/user-manager/session";
        var projection=new Dictionary<string,string>
        {
            [".proplist"]=".id,user,active"
        };
        int removed=0;
        foreach(var row in selected)
        {
            ct.ThrowIfCancellationRequested();
            var before=await query.QueryByIdAsync(parent+"/print",row.Id,projection,ct);
            if(before.Count!=1 || UmValue(before[0],".id")!=row.Id ||
                UmValue(before[0],"user")!=row.Name ||
                !before[0].ContainsKey("active") ||
                UmBoolean(before[0],"active")!=false)
                return new UmSessionRemovalResult(removed,selected.Count,false,
                    $"توقف الحذف عند الجلسة {row.Id}: تغيرت الهوية أو حالة الاتصال. "+
                    $"حُذفت {removed} من أصل {selected.Count}. حدّث القائمة.");

            try
            {
                await client.CommandAsync(parent+"/remove",
                    new Dictionary<string,string>{{".id",row.Id}},ct);
            }
            catch(RouterOsTrapException ex)
            {
                return new UmSessionRemovalResult(removed,selected.Count,false,
                    $"رفض الراوتر الحذف عند الجلسة {row.Id}: {ex.Message}. "+
                    $"حُذفت {removed} جلسة فقط.");
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException)
            {
                return new UmSessionRemovalResult(removed,selected.Count,true,
                    $"نتيجة الحذف عند الجلسة {row.Id} غير مؤكدة. "+
                    $"تأكد حذف {removed} من أصل {selected.Count} قبل الانقطاع. "+
                    "تحقق من الراوتر ولا تكرر العملية تلقائيا.");
            }

            try
            {
                var after=await query.QueryByIdAsync(parent+"/print",row.Id,projection,ct);
                if(after.Count!=0)
                    return new UmSessionRemovalResult(removed,selected.Count,true,
                        $"لم يثبت اختفاء الجلسة {row.Id}. "+
                        "أُوقفت العملية؛ تحقق من الراوتر قبل التكرار.");
                removed++;
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException
                or InvalidDataException)
            {
                return new UmSessionRemovalResult(removed,selected.Count,true,
                    $"تعذر تأكيد حذف الجلسة {row.Id}. "+
                    "أُوقفت العملية ولا تُكررها قبل تحديث السجلات.");
            }
        }
        return new UmSessionRemovalResult(removed,selected.Count,false,
            $"تم حذف {removed} جلسة منتهية وتأكيد غيابها من User Manager.");
    }
}
