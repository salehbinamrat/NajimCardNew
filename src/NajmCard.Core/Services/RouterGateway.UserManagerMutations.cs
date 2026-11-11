using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Verified User Manager account edits. This code never creates vouchers,
/// never fetches credential fields, never retries writes, and never changes
/// RADIUS or Hotspot configuration.
/// </summary>
public sealed partial class RouterGateway
{
    private static readonly IReadOnlyDictionary<string,string> UserManagerV6IdentityProjection =
        new Dictionary<string,string> { [".proplist"] = ".id,username,disabled" };
    private static readonly IReadOnlyDictionary<string,string> UserManagerV7IdentityProjection =
        new Dictionary<string,string> { [".proplist"] = ".id,name,disabled" };

    private static bool CheckedDisabled(IReadOnlyDictionary<string,string> row)
    {
        if (!row.TryGetValue("disabled",out var status))
            throw new InvalidDataException("لم يعد الراوتر حالة الحساب؛ أُوقفت العملية.");
        return status.Trim().ToLowerInvariant() switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => throw new InvalidDataException("حالة تعطيل الحساب غير معروفة؛ أُوقفت العملية.")
        };
    }

    private async Task<(RouterOsGeneration Generation,IRouterQueryClient Query,string PrintPath,
        string AccountNameField)> ValidateSelectedUserAsync(
        UserManagerAccountSelection selected,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ValidateInternalId(selected.Id);
        if (selected.Name.Length is < 1 or > 100 || selected.Name.Any(char.IsControl))
            throw new ArgumentException("اسم حساب User Manager غير صالح.", nameof(selected));
        var actual=await ReadRouterOsGenerationAsync(ct);
        if (actual!=selected.Generation)
            throw new InvalidOperationException(
                "إصدار RouterOS تغير منذ جلب القائمة؛ حدّث البيانات قبل أي تعديل.");
        if (client is not IRouterQueryClient query)
            throw new NotSupportedException(
                "يلزم دعم القراءة المقيّدة بالمعرف للتأكد من الحساب قبل التعديل.");
        return (actual,query,
            actual==RouterOsGeneration.V6?"/tool/user-manager/user/print":"/user-manager/user/print",
            actual==RouterOsGeneration.V6?"username":"name");
    }

    private static void EnsureSelectedUserMatches(
        IReadOnlyList<IReadOnlyDictionary<string,string>> rows,
        UserManagerAccountSelection selected,string nameField)
    {
        if (rows.Count!=1)
            throw new InvalidOperationException(
                "تعذر العثور على حساب واحد مطابق؛ حدّث القائمة قبل التعديل.");
        var row=rows[0];
        if (!string.Equals(Get(row,".id"),selected.Id,StringComparison.Ordinal) ||
            !string.Equals(Get(row,nameField),selected.Name,StringComparison.Ordinal) ||
            CheckedDisabled(row)!=selected.Disabled)
            throw new InvalidOperationException(
                "تغير حساب User Manager أو حالته؛ لم يُرسل أي أمر تعديل.");
    }

    /// <summary>
    /// On both RouterOS generations: targeted pre-read, one set, targeted post-read.
    /// Changes are not resent on a disconnected or ambiguous reply.
    /// </summary>
    public async Task<UserManagerMutationResult> SetUserManagerAccountDisabledAsync(
        UserManagerAccountSelection selected,bool disabled,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        var (version,query,readPath,nameField)=await ValidateSelectedUserAsync(selected,ct);
        var projection=version==RouterOsGeneration.V6?
            UserManagerV6IdentityProjection:UserManagerV7IdentityProjection;
        var current=await query.QueryByIdAsync(readPath,selected.Id,projection,ct);
        EnsureSelectedUserMatches(current,selected,nameField);
        if (selected.Disabled==disabled)
            return new UserManagerMutationResult(true,false,
                disabled?"الحساب معطل بالفعل وتم التحقق منه.":"الحساب مفعل بالفعل وتم التحقق منه.");

        var writePath=version==RouterOsGeneration.V6?
            "/tool/user-manager/user/set":"/user-manager/user/set";
        try
        {
            await client.CommandAsync(writePath,
                new Dictionary<string,string>
                {
                    [".id"]=selected.Id,["disabled"]=disabled?"yes":"no"
                },ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new UserManagerMutationResult(false,false,
                "رفض RouterOS التعديل: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException)
        {
            return new UserManagerMutationResult(false,true,
                "نتيجة تعديل الحساب غير مؤكدة؛ حدّث القائمة وافحص الحساب قبل إعادة المحاولة.");
        }

        try
        {
            var after=await query.QueryByIdAsync(readPath,selected.Id,projection,ct);
            if (after.Count==1 && Get(after[0],".id")==selected.Id &&
                Get(after[0],nameField)==selected.Name &&
                CheckedDisabled(after[0])==disabled)
                return new UserManagerMutationResult(true,false,disabled?
                    "تم تعطيل الحساب والتحقق من النتيجة.":"تم تفعيل الحساب والتحقق من النتيجة.");
            return new UserManagerMutationResult(false,true,
                "رد الراوتر لكن حالة الحساب بعد التعديل لم تثبت؛ افحص الراوتر قبل التكرار.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            return new UserManagerMutationResult(false,true,
                "تم إرسال التعديل ولكن تعذرت قراءة النتيجة؛ افحص الحساب يدويا.");
        }
    }

    /// <summary>
    /// RouterOS v7 uses a separate user-profile assignment table. This is an
    /// explicit renewal/assignment, not an implicit voucher creation action.
    /// It must verify the user, the enabled profile system and profile name;
    /// then verify the single new assignment ID returned by the router.
    /// </summary>
    public async Task<UserManagerMutationResult> AssignUserManagerV7ProfileAsync(
        UserManagerAccountSelection selected,string profile,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(selected);
        if(selected.Generation!=RouterOsGeneration.V7)
            throw new InvalidOperationException(
                "إسناد الباقات بهذا المسار مدعوم للإصدار السابع فقط.");
        if(string.IsNullOrWhiteSpace(profile) || profile.Length>100 ||
           profile.Any(char.IsControl))
            throw new ArgumentException("اسم باقة User Manager غير صالح.",nameof(profile));
        var (generation,query,readPath,nameField)=await ValidateSelectedUserAsync(selected,ct);
        if(generation!=RouterOsGeneration.V7)
            throw new InvalidOperationException("تغير إصدار RouterOS؛ لم تُرسل أي عملية.");
        var user=await query.QueryByIdAsync(readPath,selected.Id,
            UserManagerV7IdentityProjection,ct);
        EnsureSelectedUserMatches(user,selected,nameField);
        if(selected.Disabled)
            throw new InvalidOperationException(
                "الحساب معطل. فعله بعد مراجعة حالته قبل إسناد باقة جديدة.");

        var config=await client.CommandAsync("/user-manager/print",
            new Dictionary<string,string>
            {
                [".proplist"]="enabled,use-profiles"
            },ct);
        if(config.Count!=1 || Get(config[0],"enabled")!="yes" ||
           Get(config[0],"use-profiles")!="yes")
            throw new InvalidOperationException(
                "يجب تفعيل User Manager وخيار use-profiles في الراوتر قبل إسناد الباقات.");

        var profiles=await client.CommandAsync("/user-manager/profile/print",
            new Dictionary<string,string> { [".proplist"]="name" },ct);
        if(profiles.Count(x=>Get(x,"name")==profile)!=1)
            throw new InvalidOperationException(
                "الباقة المختارة غير موجودة أو غير فريدة؛ حدّث الباقات قبل الإسناد.");

        if(client is not IRouterReceiptClient receiptClient)
            throw new NotSupportedException(
                "الاتصال لا يدعم إثبات معرف الباقة الجديدة من رد RouterOS.");

        RouterCommandReply response;
        try
        {
            response=await receiptClient.CommandWithReceiptAsync(
                "/user-manager/user-profile/add",
                new Dictionary<string,string>
                {
                    ["user"]=selected.Name,["profile"]=profile
                },ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new UserManagerMutationResult(false,false,
                "رفض RouterOS إسناد الباقة: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException)
        {
            return new UserManagerMutationResult(false,true,
                "ربما تم إسناد الباقة قبل انقطاع الاتصال؛ لا تعاود الإرسال قبل فحص المستخدم.");
        }

        response.Done.TryGetValue("ret",out var newId);
        if(string.IsNullOrWhiteSpace(newId) || newId[0]!='*' ||
           newId.Length is < 2 or > 40 || newId.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))
            return new UserManagerMutationResult(false,true,
                "لم يرسل الراوتر معرفا صالحا للإسناد الجديد؛ قد تكون الباقة أضيفت بالفعل.");

        try
        {
            var result=await query.QueryByIdAsync("/user-manager/user-profile/print",
                newId,new Dictionary<string,string>
                {
                    [".proplist"]=".id,user,profile,state,end-time"
                },ct);
            if(result.Count==1 && Get(result[0],".id")==newId &&
                Get(result[0],"user")==selected.Name &&
                Get(result[0],"profile")==profile)
                return new UserManagerMutationResult(true,false,
                    "أُسندت الباقة للمستخدم وتم تأكيد سجلها في RouterOS 7.");
            return new UserManagerMutationResult(false,true,
                "نتيجة إضافة الباقة غير مؤكدة؛ راجع سجلات user-profile قبل التكرار.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            return new UserManagerMutationResult(false,true,
                "قد تكون الباقة أضيفت لكن تعذر التحقق منها؛ لا تعاود الإرسال تلقائيا.");
        }
    }
}
