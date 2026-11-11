using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// New RouterOS v7 User Manager operator account (NOT the bulk voucher printer).
/// Explicit two-stage creation: /user-manager/user/add followed by
/// /user-manager/user-profile/add. Never replay a write after an unknown outcome.
/// The account cannot be called internet-ready without an actual RADIUS login.
/// </summary>
public sealed partial class RouterGateway
{
    private static bool IsInternalRouterId(string? id) =>
        id is { Length: >= 2 and <= 40 } && id[0]=='*' &&
        id.Skip(1).All(char.IsAsciiHexDigit);

    public async Task<UserManagerV7ProvisionResult> CreateUserManagerV7AccountAsync(
        string username,string password,string profile,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(string.IsNullOrWhiteSpace(username) || username.Length>64 ||
            username.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)))
            throw new ArgumentException("اسم حساب User Manager غير صالح.",nameof(username));
        if(password is null || password.Length>128 || password.Any(char.IsControl))
            throw new ArgumentException("كلمة مرور User Manager غير صالحة.",nameof(password));
        if(string.IsNullOrWhiteSpace(profile) || profile.Length>100 ||
            profile.Any(char.IsControl))
            throw new ArgumentException("باقة User Manager غير صالحة.",nameof(profile));
        if(await ReadRouterOsGenerationAsync(ct)!=RouterOsGeneration.V7)
            throw new NotSupportedException("إنشاء الحساب بهذا المسار يدعم RouterOS 7 فقط.");

        if(client is not IRouterNameQueryClient names ||
           client is not IRouterQueryClient exact ||
           client is not IRouterReceiptClient receipts)
            throw new NotSupportedException(
                "إنشاء الحساب يتطلب استعلام اسم مقيّدا ومعرفا فوريا ورد RouterOS موثقا.");

        var state=await client.CommandAsync("/user-manager/print",
            new Dictionary<string,string> { [".proplist"]="enabled,use-profiles" },ct);
        if(state.Count!=1 || Get(state[0],"enabled")!="yes" ||
           Get(state[0],"use-profiles")!="yes")
            throw new InvalidOperationException(
                "يلزم تفعيل User Manager وخيار use-profiles قبل إنشاء الحساب.");

        var packages=await client.CommandAsync("/user-manager/profile/print",
            new Dictionary<string,string>{{".proplist"]="name"}},ct);
        if(packages.Count(x=>Get(x,"name")==profile)!=1)
            throw new InvalidOperationException(
                "الباقة غير موجودة أو غير فريدة؛ حدّث قائمة الباقات.");

        var columns=new Dictionary<string,string>{{".proplist"]=".id,name"};
        foreach(var menu in new[]
        {
            "/user-manager/user/print",
            "/ip/hotspot/user/print",
            "/ppp/secret/print"
        })
        {
            var collisions=await names.QueryByNameAsync(menu,username,columns,ct);
            if(collisions.Any(x=>Get(x,"name")==username))
                throw new InvalidOperationException(
                    "اسم المستخدم موجود مسبقا في User Manager أو Hotspot أو PPP؛ "+
                    "لم يتم إنشاء حساب مكرر.");
            if(collisions.Count>0)
                throw new InvalidDataException(
                    "أعاد الراوتر نتيجة غير متطابقة لاستعلام اسم المستخدم؛ أُوقف الإنشاء.");
        }

        RouterCommandReply creation;
        try
        {
            // The explicit empty password corresponds to username-only cards.
            // No implicit password=username mapping is ever allowed.
            creation=await receipts.CommandWithReceiptAsync(
                "/user-manager/user/add",
                new Dictionary<string,string>
                {
                    ["name"]=username,
                    ["password"]=password
                },ct);
        }
        catch(RouterOsTrapException)
        {
            return new UserManagerV7ProvisionResult(false,false,false,false,
                username,null,"رفض الراوتر إنشاء الحساب؛ تحقق من صلاحيات User Manager.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException)
        {
            return new UserManagerV7ProvisionResult(false,true,false,false,
                username,null,"نتيجة إنشاء الحساب غير مؤكدة؛ افحصه قبل إعادة المحاولة.");
        }

        creation.Done.TryGetValue("ret",out var newId);
        if(!IsInternalRouterId(newId))
            return new UserManagerV7ProvisionResult(false,true,false,false,
                username,null,"أعاد RouterOS نتيجة دون معرف حساب صالح؛ افحص الحساب قبل إعادة المحاولة.");

        try
        {
            var verified=await exact.QueryByIdAsync("/user-manager/user/print",newId!,
                new Dictionary<string,string>{{".proplist"]=".id,name,disabled"},ct);
            if(verified.Count!=1 || Get(verified[0],".id")!=newId ||
                Get(verified[0],"name")!=username || !verified[0].ContainsKey("disabled"))
                return new UserManagerV7ProvisionResult(false,true,false,false,
                    username,newId,"لم يتأكد إنشاء الحساب رغم استجابة الراوتر؛ افحصه قبل الإعادة.");
            // The account exists. Any later failure must be reported as partial,
            // never silently rolled back or retried.
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            return new UserManagerV7ProvisionResult(false,true,false,false,
                username,newId,"تعذر التحقق من الحساب الذي أرسله الراوتر؛ لا تعاود الإنشاء.");
        }

        try
        {
            var assignment=await AssignUserManagerV7ProfileAsync(
                new UserManagerAccountSelection(RouterOsGeneration.V7,newId!,
                    username,false),profile,ct);
            if(assignment.Success)
                return new UserManagerV7ProvisionResult(true,false,true,true,
                    username,newId,
                    "تم إنشاء الحساب وربط باقته والتحقق منهما. يتطلب الاتصال "+
                    "بالإنترنت اختبار RADIUS وHotspot الفعلي.");
            return new UserManagerV7ProvisionResult(false,assignment.Uncertain,
                true,false,username,newId,
                "تم إنشاء الحساب لكن الباقة غير مؤكدة: "+assignment.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or
            ObjectDisposedException or InvalidOperationException or InvalidDataException or
            NotSupportedException)
        {
            return new UserManagerV7ProvisionResult(false,true,true,false,
                username,newId,
                "تم إنشاء الحساب لكن لم يكتمل إسناد الباقة؛ راجع user-profile قبل تكرار العملية.");
        }
    }
}
