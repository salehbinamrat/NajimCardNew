using System.Globalization;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Opt-in RouterOS 7 profile/limitation CRUD. Every edit is scoped to an
/// exact ID, uses pre/post readback, never retries ambiguous writes and
/// blocks removal of profiles/limits referenced by an assignment.
/// Legacy v6 mutations need separate owner-specific verification and remain closed.
/// </summary>
public sealed partial class RouterGateway
{
    private static void ValidateUmDraft(UmPlanDraft draft,string action)
    {
        if(action is not ("add" or "set" or "remove"))
            throw new ArgumentException("إجراء إدارة الباقات غير معروف.",nameof(action));
        ArgumentNullException.ThrowIfNull(draft);
        if(draft.Kind is not ("Profile" or "Limitation"))
            throw new ArgumentException("اختر Profile أو Limitation.",nameof(draft));
        if(string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Length>80 ||
           draft.Name.Any(c=>char.IsControl(c) || c is '=' or '?' or '*'))
            throw new ArgumentException("اسم الباقة غير صالح.",nameof(draft));
        if(action=="remove")return;
        if(draft.Kind=="Profile")
        {
            if(!decimal.TryParse(draft.Price,NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,out var price) || price<0 || price>1_000_000_000)
                throw new ArgumentException(
                    "السعر يجب أن يكون رقما موجبا أو صفرا.",nameof(draft));
            if(string.IsNullOrWhiteSpace(draft.Validity) ||
               draft.Validity.Length>32 || draft.Validity.Any(char.IsControl))
                throw new ArgumentException("مدة الباقة غير صالحة.",nameof(draft));
            if(draft.StartsWhen is not ("" or "assigned" or "first-auth"))
                throw new ArgumentException(
                    "بداية صلاحية Profile يجب أن تكون assigned أو first-auth.",nameof(draft));
        }
        else
        {
            var quantities=new[]{draft.TransferLimit,draft.DownloadLimit,
                draft.UploadLimit};
            foreach(var v in quantities)
            {
                if(v.Length==0)continue;
                if(!long.TryParse(v,NumberStyles.None,
                    CultureInfo.InvariantCulture,out var n)||n<0)
                    throw new ArgumentException(
                        "حد الاستهلاك يجب أن يكون عددا صحيحا بالبايت.",nameof(draft));
            }
            foreach(var rate in new[]{draft.RateRx,draft.RateTx})
            {
                if(rate.Length==0)continue;
                if(rate.Length>22 || !rate.All(c=>char.IsAsciiDigit(c) ||
                    c is 'k' or 'M' or 'G'))
                    throw new ArgumentException(
                        "السرعة يجب أن تكون رقم RouterOS مع k أو M أو G.",nameof(draft));
            }
            if(draft.UptimeLimit.Length>32 || draft.UptimeLimit.Any(char.IsControl))
                throw new ArgumentException("حد المدة غير صالح.",nameof(draft));
            if(quantities.All(string.IsNullOrEmpty)&&
                string.IsNullOrEmpty(draft.UptimeLimit)&&
                string.IsNullOrEmpty(draft.RateRx)&&
                string.IsNullOrEmpty(draft.RateTx))
                throw new ArgumentException(
                    "أدخل قيدا واحدا على الأقل؛ لن ننشئ Limitation بلا حدود.",nameof(draft));
        }
    }

    public async Task<UmPlanMutationResult> ChangeUmPlanAsync(
        string action,UmPlanDraft draft,UmPlanRow? selected=null,CancellationToken ct=default)
    {
        ValidateUmDraft(draft,action);
        var generation=await ReadRouterOsGenerationAsync(ct);
        if(generation!=RouterOsGeneration.V7)
            throw new NotSupportedException(
                "عمليات تعديل الباقات والقيود عبر هذه الواجهة تتطلب RouterOS 7. "+
                "تعريفات v6 مرتبطة بمالك User Manager وتحتاج فحصا خاصا.");
        if(client is not IRouterQueryClient exact)
            throw new NotSupportedException(
                "الاتصال لا يدعم قراءة السجل بالمعرف والتحقق قبل التعديل.");
        if(action=="add" && client is not IRouterReceiptClient)
            throw new NotSupportedException(
                "لا يمكن إنشاء الباقة دون استقبال معرف RouterOS الجديد للتحقق منها.");

        var basePath=draft.Kind=="Profile"?"/user-manager/profile":"/user-manager/limitation";
        var readPath=basePath+"/print";
        var projection=new Dictionary<string,string> {
            [".proplist"]=draft.Kind=="Profile"
                ?".id,name,price,validity,starts-when"
                :".id,name,transfer-limit,download-limit,upload-limit,uptime-limit,rate-limit-rx,rate-limit-tx"
        };
        var command=basePath+"/"+action;
        var attrs=new Dictionary<string,string>();
        if(action=="add" || action=="set")
        {
            attrs["name"]=draft.Name;
            if(draft.Kind=="Profile")
            {
                attrs["price"]=draft.Price;
                attrs["validity"]=draft.Validity;
                if(draft.StartsWhen.Length>0)attrs["starts-when"]=draft.StartsWhen;
            }
            else
            {
                void Put(string key,string value)
                {
                    if(value.Length>0)attrs[key]=value;
                }
                Put("transfer-limit",draft.TransferLimit);
                Put("download-limit",draft.DownloadLimit);
                Put("upload-limit",draft.UploadLimit);
                Put("uptime-limit",draft.UptimeLimit);
                Put("rate-limit-rx",draft.RateRx);
                Put("rate-limit-tx",draft.RateTx);
            }
        }
        if(action=="add")
        {
            var existing=await client.CommandAsync(readPath,
                new Dictionary<string,string> {[".proplist"]="name"},ct);
            if(existing.Any(x=>!x.ContainsKey("name")))
                throw new InvalidDataException(
                    "تعذرت قراءة أسماء الباقات قبل فحص التكرار.");
            if(existing.Any(x=>string.Equals(UmValue(x,"name"),draft.Name,
                StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "يوجد Profile/Limit بنفس الاسم. لم يُرسل أمر الإنشاء.");
        }
        else
        {
            if(selected is null || selected.Kind!=draft.Kind)
                throw new ArgumentException("حدد سجلا حقيقيا من الجدول.");
            ValidateInternalId(selected.Id);
            var before=await exact.QueryByIdAsync(readPath,selected.Id,projection,ct);
            if(before.Count!=1 || UmValue(before[0],".id")!=selected.Id ||
               UmValue(before[0],"name")!=selected.Name)
                throw new InvalidOperationException(
                    "تغير السجل منذ قراءته. حدّث الباقات قبل التعديل.");
            attrs[".id"]=selected.Id;
            if(action=="set" && !string.Equals(selected.Name,draft.Name,
                StringComparison.OrdinalIgnoreCase))
            {
                var names=await client.CommandAsync(readPath,
                    new Dictionary<string,string> {[".proplist"]=".id,name"},ct);
                if(names.Any(x=>UmValue(x,".id")!=selected.Id &&
                    string.Equals(UmValue(x,"name"),draft.Name,
                        StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        "الاسم الجديد مستخدم بالفعل. لم يُرسل التعديل.");
            }
            if(action=="remove")
            {
                var relationships=await client.CommandAsync(
                    "/user-manager/profile-limitation/print",
                    new Dictionary<string,string> {
                        [".proplist"]="profile,limitation"
                    },ct);
                if(relationships.Any(x=>draft.Kind=="Profile"
                        ?string.Equals(UmValue(x,"profile"),selected.Name,
                            StringComparison.OrdinalIgnoreCase)
                        :string.Equals(UmValue(x,"limitation"),selected.Name,
                            StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        "القيد أو الباقة مرتبط بسجلات أخرى؛ ألغ الروابط أولا ولا تحذفه قسرا.");
                if(draft.Kind=="Profile")
                {
                    var assignments=await client.CommandAsync(
                        "/user-manager/user-profile/print",
                        new Dictionary<string,string> {[".proplist"]="profile"},ct);
                    if(assignments.Any(x=>string.Equals(UmValue(x,"profile"),selected.Name,
                        StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(
                            "توجد إسنادات مستخدمين لهذه الباقة. الحذف موقوف لحمايتهم.");
                }
            }
        }

        string? createdId=null;
        try
        {
            if(action=="add")
            {
                var response=await ((IRouterReceiptClient)client).CommandWithReceiptAsync(
                    command,attrs,ct);
                response.Done.TryGetValue("ret",out createdId);
                if(string.IsNullOrWhiteSpace(createdId) ||
                   createdId[0]!='*'||createdId.Length>40||
                   createdId.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))
                    return new UmPlanMutationResult(false,true,
                        "ربما أُنشئ السجل لكن الراوتر لم يرجع معرفا صالحا؛ لا تعد الإنشاء.");
            }
            else await client.CommandAsync(command,attrs,ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new UmPlanMutationResult(false,false,
                "رفض RouterOS الإجراء: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            return new UmPlanMutationResult(false,true,
                "نتيجة الأمر غير مؤكدة بعد انقطاع الاتصال أو توقفه؛ لا تكرر العملية.");
        }

        try
        {
            var id=action=="add"?createdId!:selected!.Id;
            var after=await exact.QueryByIdAsync(readPath,id,projection,ct);
            if(action=="remove")
                return after.Count==0
                    ?new UmPlanMutationResult(true,false,"حُذف السجل وتأكد غيابه من RouterOS.")
                    :new UmPlanMutationResult(false,true,
                        "أجاب الراوتر لكن السجل ما زال موجودا؛ راجع قبل التكرار.");
            if(after.Count!=1 || UmValue(after[0],".id")!=id ||
               !string.Equals(UmValue(after[0],"name"),draft.Name,
                StringComparison.Ordinal))
                return new UmPlanMutationResult(false,true,
                    "أرسل الراوتر رد نجاح لكن لم تثبت مطابقة الاسم والمعرف الجديد.");

            // The RouterOS representation of rates/duration may be normalized.
            // Verify every field that was explicitly sent, if the router returned
            // it. Missing fields after a write are NOT a successful confirmation.
            foreach(var (key,value) in attrs)
            {
                if(key==".id")continue;
                if(!after[0].ContainsKey(key))
                    return new UmPlanMutationResult(false,true,
                        "تعذر إثبات أحد حقول الباقة بعد تعديلها؛ افحص RouterOS قبل التكرار.");
                if(key is "price" or "validity" or "transfer-limit" or "download-limit"
                    or "upload-limit" or "uptime-limit" or "rate-limit-rx"
                    or "rate-limit-tx" or "starts-when")
                {
                    if(!string.Equals(UmValue(after[0],key),value,
                        StringComparison.OrdinalIgnoreCase))
                        return new UmPlanMutationResult(false,true,
                            "قام RouterOS بتطبيع قيمة إحدى الخانات؛ افحص القيم الفعلية قبل اعتماد الباقة.");
                }
            }
            return new UmPlanMutationResult(true,false,
                action=="add"?"أُنشئ السجل وتأكدت بياناته من الراوتر.":
                    "عُدل السجل وتأكدت بياناته من الراوتر.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            return new UmPlanMutationResult(false,true,
                "ربما تم التعديل لكن تعذرت قراءة التأكيد؛ لا تعاود العملية.");
        }
    }
}
