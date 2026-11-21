using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    public async Task<string> ReadRouterIdentityAsync(CancellationToken ct=default)
    {
        using var measurement=VoucherPipelineOperation.Measure(VoucherPipelineStage.Identity);
        return await (client is ResilientRouterSession session?session.BindRouterIdentityAsync(ct):
            RouterDeviceIdentity.ReadAsync(client,ct));
    }

    public async Task VerifyRouterIdentityAsync(string expected,CancellationToken ct=default)
    {
        using var measurement=VoucherPipelineOperation.Measure(VoucherPipelineStage.Identity);
        if(string.IsNullOrWhiteSpace(expected))
            throw new InvalidOperationException("ملكية الدفعة القديمة غير معروفة؛ لا تُنسب تلقائيا إلى الراوتر الحالي.");
        if(client is ResilientRouterSession session)
        {
            // The first check binds this session; every replacement connection
            // is independently verified inside ExecuteAsync before any command.
            await session.EnsureRouterIdentityAsync(expected,ct);
        }
        else if(await RouterDeviceIdentity.ReadAsync(client,ct)!=expected)throw new RouterIdentityMismatchException();
    }

    public async Task<VoucherReconciliation> ReconcileVoucherAsync(Voucher card,CancellationToken ct=default)
    {
        await VerifyRouterIdentityAsync(card.RouterId,ct);
        if(client is not IRouterNameQueryClient query)
            throw new NotSupportedException("المطابقة تتطلب قراءة مستقلة بالاسم من الراوتر.");
        var sources=new[]{("hotspot","/ip/hotspot/user/print","name"),
            ("pppoe","/ppp/secret/print","name"),("userman","/tool/user-manager/user/print","username")};
        IReadOnlyDictionary<string,string>? matched=null;
        bool conflict=false;
        static bool MissingMenu(RouterOsTrapException ex)=>
            ex.Message.Contains("no such command",StringComparison.OrdinalIgnoreCase)||
            ex.Message.Contains("bad command name",StringComparison.OrdinalIgnoreCase)||
            ex.Message.Contains("unknown command",StringComparison.OrdinalIgnoreCase);
        foreach(var (service,path,field) in sources)
        {
            // Read full fields of one name, including our ownership marker.
            // Any inaccessible menu/partial reply prevents proof of absence.
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
            var actualField=field;
            try{rows=await query.QueryByNameAsync(path,card.Username,cancellationToken:ct);}
            catch(RouterOsTrapException ex) when(MissingMenu(ex)&&service!=card.Service)
            {
                if(service!="userman")continue;
                // A missing v6 menu is not evidence that v7 users are absent.
                actualField="name";
                try{rows=await query.QueryByNameAsync("/user-manager/user/print",card.Username,cancellationToken:ct);}
                catch(RouterOsTrapException modern) when(MissingMenu(modern)){continue;}
            }
            if(rows.Any(row=>Get(row,actualField)!=card.Username||string.IsNullOrWhiteSpace(Get(row,".id"))))
                return new("UNKNOWN","رد المطابقة ناقص أو لم يلتزم باستعلام الاسم.");
            if(rows.Count>0)
            {
                if(service!=card.Service||rows.Count!=1)conflict=true;
                else matched=rows[0];
            }
        }
        await VerifyRouterIdentityAsync(card.RouterId,ct);
        if(conflict)return new("CONFLICT","يوجد اسم مطابق في خدمة أخرى أو أكثر من حساب؛ يلزم فحص يدوي.");
        if(matched is null)return new("READY","ثبت غياب الاسم من خدمات الراوتر؛ يلزم التحقق مجددا عند الاستئناف.");
        var id=Get(matched,".id");
        if(Get(matched,"comment")!=card.DeliveryTag)
            return new("CONFLICT","يوجد حساب بالاسم نفسه دون علامة إصدار هذه الدفعة؛ لن يُعاد إنشاؤه.",id);
        if(!UserManagerV6Usage.TryBoolean(Get(matched,"disabled"),out var disabled)||disabled)
            return new("UNKNOWN","الحساب موجود، لكن تفعيله الإداري غير مؤكد.",id);
        if(card.Service=="userman")
        {
            if(!UserManagerV6QuotaCatalog.TryOwner(matched,out var owner)||owner!=card.Customer)
                return new("CONFLICT","مالك الحساب المقروء لا يطابق الدفعة.",id);
            if(!UserManagerV6Usage.TryBoolean(Get(matched,"incomplete"),out var incomplete)||incomplete||
                Get(matched,"actual-profile")!=card.Profile)
                return new("UNKNOWN","الحساب أُنشئ لكن تفعيل الباقة غير مؤكد؛ لا يُعاد إنشاؤه أو تفعيله تلقائيا.",id);
        }
        else if(Get(matched,"profile")!=card.Profile||
            card.Service=="pppoe"&&Get(matched,"service")!="pppoe")
            return new("CONFLICT","الباقة أو الخدمة لا تطابق الكرت المحفوظ.",id);
        return new("SENT","تأكد وجود حساب هذه الدفعة وباقته على الراوتر الصحيح.",id);
    }
}
