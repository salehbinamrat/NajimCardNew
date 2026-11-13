using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    /// <summary>
    /// Owner-checked RouterOS 6 profile/limitation link. The legacy router may
    /// store either ID or name in the relation table; both are verified.
    /// Ambiguous shared names across owners are rejected before writing.
    /// </summary>
    public async Task<UmPlanMutationResult> LinkUmProfileLimitationV6Async(
        string profile,string limitation,string owner,CancellationToken ct=default)
    {
        ValidateLegacyOwner(owner);
        if(string.IsNullOrWhiteSpace(profile) || profile.Length>80 ||
           string.IsNullOrWhiteSpace(limitation) || limitation.Length>80 ||
           profile.Any(char.IsControl)||limitation.Any(char.IsControl))
            throw new ArgumentException("يجب اختيار باقة وقيد حقيقيين.");
        if(await ReadRouterOsGenerationAsync(ct)!=RouterOsGeneration.V6)
            throw new NotSupportedException("هذه عملية ربط خاصة بإصدار RouterOS 6.");
        if(client is not IRouterReceiptClient receipt || client is not IRouterQueryClient query)
            throw new NotSupportedException(
                "يلزم الحصول على معرف الربط الجديد وقراءة نتيجته للتحقق.");

        var customers=await ReadUserManagerV6CustomersAsync(ct);
        if(customers.Count(x=>x.Login==owner)!=1)
            throw new InvalidOperationException("مالك القيد غير معروف أو غير فريد.");

        const string profilePath="/tool/user-manager/profile/print";
        const string limitPath="/tool/user-manager/profile/limitation/print";
        const string relationPath="/tool/user-manager/profile/profile-limitation/print";
        var projection=new Dictionary<string,string>{{".proplist",".id,name,owner"}};
        var profiles=await client.CommandAsync(profilePath,projection,ct);
        var limits=await client.CommandAsync(limitPath,projection,ct);
        if(profiles.Any(x=>!x.ContainsKey(".id") ||
            !x.ContainsKey("name") || !x.ContainsKey("owner")) ||
            limits.Any(x=>!x.ContainsKey(".id") ||
            !x.ContainsKey("name") || !x.ContainsKey("owner")))
            throw new InvalidDataException(
                "قراءة الباقات أو القيود لا تتضمن المالك؛ لم يُرسل الربط.");

        var selectedProfile=profiles.Where(x=>UmValue(x,"name")==profile &&
            UmValue(x,"owner")==owner).ToArray();
        var selectedLimit=limits.Where(x=>UmValue(x,"name")==limitation &&
            UmValue(x,"owner")==owner).ToArray();
        if(selectedProfile.Length!=1 || selectedLimit.Length!=1 ||
           profiles.Count(x=>UmValue(x,"name")==profile)!=1 ||
           limits.Count(x=>UmValue(x,"name")==limitation)!=1)
            throw new InvalidOperationException(
                "الباقة أو القيد غير فريد بين حسابات الملاك؛ لا يمكن الربط بأمان.");
        var profileId=UmValue(selectedProfile[0],".id");
        var limitId=UmValue(selectedLimit[0],".id");
        ValidateInternalId(profileId);
        ValidateInternalId(limitId);

        var existing=await client.CommandAsync(relationPath,
            new Dictionary<string,string>{{".proplist",".id,profile,limitation"}},ct);
        if(existing.Any(x=>!x.ContainsKey("profile") || !x.ContainsKey("limitation")))
            throw new InvalidDataException(
                "روابط User Manager الحالية ناقصة؛ لم يُرسل الربط.");
        bool ProfileMatches(IReadOnlyDictionary<string,string> row)=>
            UmValue(row,"profile")==profile || UmValue(row,"profile")==profileId;
        bool LimitMatches(IReadOnlyDictionary<string,string> row)=>
            UmValue(row,"limitation")==limitation || UmValue(row,"limitation")==limitId;
        if(existing.Any(x=>ProfileMatches(x)&&LimitMatches(x)))
            return new UmPlanMutationResult(true,false,
                "القيد مربوط بالفعل؛ لم يُرسل أمر مكرر.");

        string? id;
        try
        {
            var reply=await receipt.CommandWithReceiptAsync(
                "/tool/user-manager/profile/profile-limitation/add",
                new Dictionary<string,string>{{"profile",profile},{"limitation",limitation}},ct);
            reply.Done.TryGetValue("ret",out id);
            if(string.IsNullOrWhiteSpace(id) || id.Length>40 ||
               id[0]!='*'||id.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))
            {
                InvalidateUmPlansCache();
                return new UmPlanMutationResult(false,true,
                    "الربط ربما تم دون معرف تأكيد. افحص الراوتر قبل التكرار.");
            }
        }
        catch(RouterOsTrapException ex)
        {
            return new UmPlanMutationResult(false,false,
                "رفض RouterOS 6 الربط: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            InvalidateUmPlansCache();
            return new UmPlanMutationResult(false,true,
                "نتيجة الربط غير مؤكدة بسبب انقطاع الاتصال.");
        }
        InvalidateUmPlansCache();
        try
        {
            var after=await query.QueryByIdAsync(relationPath,id,
                new Dictionary<string,string>{{".proplist",".id,profile,limitation"}},ct);
            return after.Count==1 && UmValue(after[0],".id")==id &&
                ProfileMatches(after[0]) && LimitMatches(after[0])
                ?new UmPlanMutationResult(true,false,
                    "تم ربط القيد وتأكدت بياناته من RouterOS 6.")
                :new UmPlanMutationResult(false,true,
                    "رد الراوتر لكن لم تتأكد العلاقة الجديدة.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            return new UmPlanMutationResult(false,true,
                "ربما تم ربط القيد لكن فشلت قراءة التأكيد.");
        }
    }
}
