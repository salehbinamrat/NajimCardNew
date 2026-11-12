using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    /// <summary>
    /// Explicit and verified RouterOS v7 profile to limitation association.
    /// Does not retry after unknown write results, and does not modify v6.
    /// </summary>
    public async Task<UmPlanMutationResult> LinkUmProfileLimitationAsync(
        string profile,string limitation,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(profile) || profile.Length>80 ||
           string.IsNullOrWhiteSpace(limitation) || limitation.Length>80 ||
           profile.Any(char.IsControl)||limitation.Any(char.IsControl))
            throw new ArgumentException("اختر باقة وقيدا حقيقيين قبل الربط.");
        if(await ReadRouterOsGenerationAsync(ct)!=RouterOsGeneration.V7)
            throw new NotSupportedException(
                "ربط Profile وLimitation بهذه الواجهة متاح للإصدار السابع فقط.");
        if(client is not IRouterReceiptClient receipt || client is not IRouterQueryClient query)
            throw new NotSupportedException(
                "يلزم استقبال معرف الإضافة والقراءة المقيّدة به لتأكيد الربط.");

        var profiles=await client.CommandAsync("/user-manager/profile/print",
            new Dictionary<string,string>{{".proplist","name"}},ct);
        var limits=await client.CommandAsync("/user-manager/limitation/print",
            new Dictionary<string,string>{{".proplist","name"}},ct);
        if(profiles.Count(x=>UmValue(x,"name")==profile)!=1 ||
           limits.Count(x=>UmValue(x,"name")==limitation)!=1)
            throw new InvalidOperationException(
                "لم تتأكد الباقة أو القيد المحدد بالاسم في الراوتر.");

        var existing=await client.CommandAsync(
            "/user-manager/profile-limitation/print",
            new Dictionary<string,string>{{".proplist","profile,limitation"}},ct);
        if(existing.Any(x=>UmValue(x,"profile")==profile&&
            UmValue(x,"limitation")==limitation))
            return new UmPlanMutationResult(true,false,
                "هذا القيد مرتبط بالباقة بالفعل؛ لم يُرسل أمر إضافي.");

        string? id;
        try
        {
            var done=await receipt.CommandWithReceiptAsync(
                "/user-manager/profile-limitation/add",
                new Dictionary<string,string>{{"profile",profile},{"limitation",limitation}},ct);
            done.Done.TryGetValue("ret",out id);
            if(string.IsNullOrWhiteSpace(id)||id[0]!='*'||id.Length>40||
               id.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))
                return new UmPlanMutationResult(false,true,
                    "ربما تم الربط لكن RouterOS لم يرجع معرفا صالحا. لا تعاود الربط تلقائيا.");
        }
        catch(RouterOsTrapException ex)
        {
            return new UmPlanMutationResult(false,false,"رفض RouterOS الربط: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return new UmPlanMutationResult(false,true,
                "انقطع الاتصال بعد إرسال الربط؛ تأكد من الراوتر قبل إعادة المحاولة.");
        }

        try
        {
            var after=await query.QueryByIdAsync(
                "/user-manager/profile-limitation/print",id,
                new Dictionary<string,string>{{".proplist",".id,profile,limitation"}},ct);
            return after.Count==1 && UmValue(after[0],".id")==id &&
                UmValue(after[0],"profile")==profile &&
                UmValue(after[0],"limitation")==limitation
                ?new UmPlanMutationResult(true,false,
                    "تم ربط القيد بالباقة وتأكدت العلاقة من RouterOS.")
                :new UmPlanMutationResult(false,true,
                    "ربما أُنشئ الربط لكن لم تتأكد العلاقة؛ لا تعاود الأمر.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            return new UmPlanMutationResult(false,true,
                "ربما أُنشئ الربط لكن تعذرت القراءة النهائية؛ لا تعاود الأمر.");
        }
    }
}
