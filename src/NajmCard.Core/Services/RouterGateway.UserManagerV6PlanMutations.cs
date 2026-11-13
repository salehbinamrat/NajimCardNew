using System.Globalization;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// RouterOS 6 User Manager profile/limitation edits. Legacy objects have an
/// explicit customer owner and "starts-at", not the v7 "starts-when".
/// A write never happens unless owner, identity, and supported transport
/// have been independently verified. No mutation retries on an unknown reply.
/// </summary>
public sealed partial class RouterGateway
{
    private static bool SameLegacyIdentity(
        IReadOnlyDictionary<string,string> row,UmPlanRow selected) =>
        string.Equals(UmValue(row,".id"),selected.Id,StringComparison.Ordinal) &&
        string.Equals(UmValue(row,"name"),selected.Name,StringComparison.Ordinal) &&
        string.Equals(UmValue(row,"owner"),selected.Owner,StringComparison.Ordinal);

    private static string LegacyStartsAt(string value)=>value switch
    {
        "assigned" or "now" or ""=>"now",
        "first-auth" or "logon"=>"logon",
        _=>throw new ArgumentException("بداية صلاحية RouterOS 6 غير معروفة.")
    };

    private static void ValidateLegacyOwner(string owner)
    {
        if(string.IsNullOrWhiteSpace(owner) || owner.Length>80 ||
           owner.Any(c=>char.IsControl(c) || c is '=' or '?' or '*'))
            throw new NotSupportedException(
                "يلزم اختيار مالك User Manager v6 (owner) موثوق قبل إدارة الباقة.");
    }

    private async Task<UmPlanMutationResult> ChangeUmPlanV6Async(
        string action,UmPlanDraft draft,UmPlanRow? selected,CancellationToken ct)
    {
        ValidateLegacyOwner(draft.Owner);
        if(client is not IRouterQueryClient query)
            throw new NotSupportedException(
                "لا يمكن تعديل v6 دون قراءة السجل بالمعرف قبل الأمر وبعده.");
        if(action=="add" && client is not IRouterReceiptClient)
            throw new NotSupportedException(
                "إنشاء v6 يتطلب تأكيد المعرف الجديد من RouterOS.");

        // Confirm the customer exists; do not infer ownership from a cached
        // creator profile or a similarly named profile of another customer.
        var customers=await ReadUserManagerV6CustomersAsync(ct);
        if(customers.Count(x=>string.Equals(x.Login,draft.Owner,
            StringComparison.Ordinal))!=1)
            throw new InvalidOperationException(
                "مالك الباقة غير موجود بصورة فريدة في حسابات User Manager v6.");

        var root=draft.Kind=="Profile"
            ?"/tool/user-manager/profile"
            :"/tool/user-manager/profile/limitation";
        var print=root+"/print";
        var attrs=new Dictionary<string,string>(StringComparer.Ordinal);
        var projection=new Dictionary<string,string> {
            [".proplist"]=draft.Kind=="Profile"
                ?".id,name,owner,price,validity,starts-at"
                :".id,name,owner,transfer-limit,download-limit,upload-limit,uptime-limit,rate-limit-rx,rate-limit-tx"
        };
        if(action is "add" or "set")
        {
            attrs["name"]=draft.Name;
            if(action=="add")attrs["owner"]=draft.Owner;
            if(draft.Kind=="Profile")
            {
                attrs["price"]=draft.Price;
                attrs["validity"]=draft.Validity;
                attrs["starts-at"]=LegacyStartsAt(draft.StartsWhen);
            }
            else
            {
                void Put(string key,string value)
                {
                    if(!string.IsNullOrWhiteSpace(value))attrs[key]=value;
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
            var names=await client.CommandAsync(print,
                new Dictionary<string,string>{{".proplist",".id,name,owner"}},ct);
            if(names.Any(x=>!x.ContainsKey(".id") || !x.ContainsKey("name") ||
                !x.ContainsKey("owner")))
                throw new InvalidDataException(
                    "قراءة أسماء أو ملاك الباقات غير مكتملة؛ لن يُرسل الأمر.");
            if(names.Any(x=>UmValue(x,"owner")==draft.Owner &&
                string.Equals(UmValue(x,"name"),draft.Name,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "الاسم مستخدم للمالك نفسه. لم يُرسل الإنشاء.");
        }
        else
        {
            if(selected is null || selected.Kind!=draft.Kind ||
               selected.Owner!=draft.Owner)
                throw new InvalidOperationException(
                    "اختيار السجل أو مالكه تغير؛ حدّث القائمة قبل أي تعديل.");
            ValidateInternalId(selected.Id);
            var before=await query.QueryByIdAsync(print,selected.Id,projection,ct);
            if(before.Count!=1 || !SameLegacyIdentity(before[0],selected))
                throw new InvalidOperationException(
                    "تعذر إثبات المعرف والاسم والمالك الحالي. لم يُرسل تعديل.");
            if(action=="set")
            {
                var names=await client.CommandAsync(print,
                    new Dictionary<string,string>{{".proplist",".id,name,owner"}},ct);
                if(names.Any(x=>!x.ContainsKey(".id") || !x.ContainsKey("name") ||
                    !x.ContainsKey("owner")))
                    throw new InvalidDataException(
                        "لا يمكن التأكد من تكرار الاسم بسبب رد ناقص.");
                if(names.Any(x=>UmValue(x,".id")!=selected.Id &&
                    UmValue(x,"owner")==draft.Owner &&
                    string.Equals(UmValue(x,"name"),draft.Name,
                        StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        "يوجد سجل آخر بهذا الاسم والمالك.");
            }
            else
            {
                // Never delete a referenced limitation or a profile assigned
                // to a customer. Check names AND internal IDs used by v6 links.
                var links=await client.CommandAsync(
                    "/tool/user-manager/profile/profile-limitation/print",
                    new Dictionary<string,string>{{".proplist","profile,limitation"}},ct);
                if(links.Any(x=>!x.ContainsKey("profile") || !x.ContainsKey("limitation")))
                    throw new InvalidDataException(
                        "روابط الباقات ناقصة؛ الحذف موقوف لحمايتها.");
                var reference=draft.Kind=="Profile"?"profile":"limitation";
                if(links.Any(x=>
                    string.Equals(UmValue(x,reference),selected.Name,
                        StringComparison.OrdinalIgnoreCase) ||
                    UmValue(x,reference)==selected.Id))
                    throw new InvalidOperationException(
                        "لا يمكن حذف سجل مرتبط بباقة أو قيد آخر. فك الارتباط أولا.");
                if(draft.Kind=="Profile")
                {
                    // Legacy user actual-profile is a safety signal. If
                    // absent from an existing row, never assume unassigned.
                    var users=await client.CommandAsync("/tool/user-manager/user/print",
                        new Dictionary<string,string>{{".proplist","actual-profile"}},ct);
                    if(users.Any(x=>!x.ContainsKey("actual-profile")))
                        throw new InvalidDataException(
                            "تعذر تأكيد إسنادات المستخدمين؛ الحذف موقوف.");
                    if(users.Any(x=>
                        string.Equals(UmValue(x,"actual-profile"),selected.Name,
                            StringComparison.OrdinalIgnoreCase) ||
                        UmValue(x,"actual-profile")==selected.Id))
                        throw new InvalidOperationException(
                            "توجد كروت مرتبطة بهذه الباقة؛ لا يمكن حذفها.");
                }
            }
            attrs[".id"]=selected.Id;
        }

        string? createdId=null;
        try
        {
            if(action=="add")
            {
                var response=await ((IRouterReceiptClient)client).CommandWithReceiptAsync(
                    root+"/add",attrs,ct);
                response.Done.TryGetValue("ret",out createdId);
                if(string.IsNullOrWhiteSpace(createdId) || createdId.Length>40 ||
                   createdId[0]!='*' ||
                   createdId.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))
                {
                    InvalidateCreatorCatalog();
                    InvalidateUmPlansCache();
                    return new UmPlanMutationResult(false,true,
                        "قد تكون الإضافة تمت دون معرف مؤكد. افحص الراوتر ولا تكرر.");
                }
            }
            else await client.CommandAsync(root+"/"+action,attrs,ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new UmPlanMutationResult(false,false,"رفض RouterOS 6 الأمر: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            InvalidateCreatorCatalog();
            InvalidateUmPlansCache();
            return new UmPlanMutationResult(false,true,
                "انقطع تأكيد العملية. تحقق من RouterOS قبل أي محاولة أخرى.");
        }

        InvalidateCreatorCatalog();
        InvalidateUmPlansCache();
        try
        {
            var id=action=="add"?createdId!:selected!.Id;
            var after=await query.QueryByIdAsync(print,id,projection,ct);
            if(action=="remove")
                return after.Count==0
                    ?new UmPlanMutationResult(true,false,
                        "تم الحذف وتأكد غياب السجل من RouterOS 6.")
                    :new UmPlanMutationResult(false,true,
                        "أُرسل الحذف لكن السجل ما زال ظاهرا. لا تكرر دون تحقق.");
            if(after.Count!=1 || UmValue(after[0],".id")!=id ||
               UmValue(after[0],"name")!=draft.Name ||
               UmValue(after[0],"owner")!=draft.Owner)
                return new UmPlanMutationResult(false,true,
                    "لم تتأكد مطابقة المعرف والاسم والمالك بعد العملية.");
            foreach(var (key,value) in attrs)
            {
                if(key==".id")continue;
                if(!after[0].ContainsKey(key) ||
                    !string.Equals(UmValue(after[0],key),value,
                        StringComparison.OrdinalIgnoreCase))
                    return new UmPlanMutationResult(false,true,
                        "رد RouterOS مختلف عن القيمة المرسلة؛ راجع السجل قبل التكرار.");
            }
            return new UmPlanMutationResult(true,false,
                action=="add"?"أضيفت الباقة وتأكد اسمها ومالكها وحقولها.":
                    "تم التعديل وتأكدت حقوله من User Manager v6.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            return new UmPlanMutationResult(false,true,
                "ربما تمت العملية لكن فشل التحقق النهائي. لا تكرر تلقائيا.");
        }
    }
}
