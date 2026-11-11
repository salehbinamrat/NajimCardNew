using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// A read-only local NAS diagnostic, not a real authentication attempt.
/// Remote NAS, external RADIUS servers and shared secrets require an
/// independent live test; no configuration is changed here.
/// </summary>
public sealed record UserManagerV7RadiusReport(
    bool ConfigurationReady,bool LiveLoginVerified,
    IReadOnlyList<string> Issues,string Message);

public sealed partial class RouterGateway
{
    public async Task<UserManagerV7RadiusReport> DiagnoseUserManagerV7RadiusAsync(
        CancellationToken ct=default)
    {
        if(await ReadRouterOsGenerationAsync(ct)!=RouterOsGeneration.V7)
            throw new NotSupportedException(
                "تشخيص RADIUS هذا مخصص لـ RouterOS 7؛ استخدم فحص الإصدار السادس منفصلا.");

        var issues=new List<string>();
        var state=await client.CommandAsync("/user-manager/print",
            new Dictionary<string,string> { [".proplist"]="enabled,use-profiles" },ct);
        if(state.Count!=1 || !IsYes(Get(state[0],"enabled").ToLowerInvariant()))
            issues.Add("خدمة User Manager غير مفعلة أو لم يمكن إثبات تفعيلها.");
        if(state.Count!=1 || !IsYes(Get(state[0],"use-profiles").ToLowerInvariant()))
            issues.Add("خيار use-profiles غير مفعل أو غير معروف.");

        var nas=await client.CommandAsync("/user-manager/router/print",
            new Dictionary<string,string> { [".proplist"]="name,address,disabled" },ct);
        if(!nas.Any(x=>IsNo(Get(x,"disabled").Trim().ToLowerInvariant()) &&
            !string.IsNullOrWhiteSpace(Get(x,"address"))))
            issues.Add("لا يوجد NAS مسجل ومفعل بعنوان صالح في User Manager v7.");

        var servers=await client.CommandAsync("/ip/hotspot/print",
            new Dictionary<string,string> { [".proplist"]="name,profile,disabled" },ct);
        var active=servers.Where(x=>IsNo(Get(x,"disabled").Trim().ToLowerInvariant())).ToArray();
        if(active.Length==0)
            issues.Add("لا يوجد سيرفر Hotspot محلي مفعل؛ إن كان NAS خارجيا فافحصه منفصلا.");

        var profiles=await client.CommandAsync("/ip/hotspot/profile/print",
            new Dictionary<string,string> { [".proplist"]="name,use-radius,radius-accounting" },ct);
        foreach(var server in active)
        {
            var name=Get(server,"profile");
            var matching=profiles.Where(x=>Get(x,"name")==name).ToArray();
            if(matching.Length!=1 || !IsYes(Get(matching[0],"use-radius").ToLowerInvariant()))
                issues.Add("ملف Hotspot «"+name+"» لا يثبت استخدام RADIUS.");
            if(matching.Length==1 && !IsYes(Get(matching[0],"radius-accounting").Trim().ToLowerInvariant()))
                issues.Add("ملف Hotspot «"+name+"» لا يثبت تفعيل RADIUS accounting؛ لن يمكن اعتماد حدود الاستهلاك.");
        }

        var radius=await client.CommandAsync("/radius/print",
            new Dictionary<string,string> { [".proplist"]="service,address,disabled" },ct);
        if(!radius.Any(x=>IsNo(Get(x,"disabled").Trim().ToLowerInvariant()) &&
            !string.IsNullOrWhiteSpace(Get(x,"address")) &&
            Get(x,"service").Split(',',StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries).Any(s=>
                    string.Equals(s,"hotspot",StringComparison.OrdinalIgnoreCase))))
            issues.Add("لا يوجد عميل RADIUS مفعل لخدمة hotspot.");

        var ready=issues.Count==0;
        var message=ready
            ?"فحوص إعدادات User Manager وHotspot وRADIUS وNAS ناجحة. "+
             "لم يتم اختبار تسجيل دخول فعلي؛ تحقق من السر المشترك ومسار الشبكة قبل اعتبار الكروت صالحة."
            :"إعدادات User Manager v7 تحتاج تصحيحا قبل الاعتماد. "+
             "لم يتم اختبار تسجيل دخول فعلي عبر RADIUS.";
        return new UserManagerV7RadiusReport(ready,false,issues,message);
    }
}
