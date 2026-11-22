using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public sealed record HotspotVoucherDeliveryResult(bool Success,bool UncertainOutcome,string? UserId,string? Error);

public sealed partial class RouterGateway
{
    private readonly SemaphoreSlim _voucherBatchGate=new(1,1);
    public async Task<IDisposable> EnterVoucherBatchAsync(CancellationToken ct=default)
    {await _voucherBatchGate.WaitAsync(ct);return new ReadLease(_voucherBatchGate);}

    private readonly HashSet<string> _fullVoucherReadback=new(StringComparer.Ordinal);
    private async Task<string?> VerifyIssuedVoucherAsync(Voucher voucher,string id,string service,
        string customer,CancellationToken ct)
    {
        using var measurement=VoucherPipelineOperation.Measure(VoucherPipelineStage.Verification);
        if(client is not IRouterQueryClient query)return "لا تتوفر قراءة مستقلة لتأكيد الحساب وباقته؛ راجع الراوتر.";
        var path=service switch{"userman"=>"/tool/user-manager/user/print","pppoe"=>"/ppp/secret/print",_=>"/ip/hotspot/user/print"};
        var fields=service=="userman"?".id,username,customer,owner,actual-profile":
            service=="pppoe"?".id,name,profile,service,disabled":".id,name,profile,disabled";
        string nameKey=service=="userman"?"username":"name",profileKey=service=="userman"?"actual-profile":"profile";
        bool Complete(IReadOnlyDictionary<string,string> row)=>row.ContainsKey(".id")&&row.ContainsKey(nameKey)&&row.ContainsKey(profileKey)&&
            (service=="userman"?UserManagerV6QuotaCatalog.TryOwner(row,out _):
                row.ContainsKey("disabled")&&(service!="pppoe"||row.ContainsKey("service")));
        try
        {
            var evidence=await VoucherReadbackPoller.VerifyAsync(async token=>
            {
                bool full;lock(_fullVoucherReadback)full=_fullVoucherReadback.Contains(path);
                var rows=await query.QueryByIdAsync(path,id,full?null:new Dictionary<string,string>{{".proplist",fields}},token);
                if(!full&&rows.Count==1&&!Complete(rows[0]))
                {
                    rows=await query.QueryByIdAsync(path,id,cancellationToken:token);
                    // Learn compatibility only from a complete, exact record.
                    if(rows.Count==1&&Complete(rows[0])&&Get(rows[0],".id")==id)
                        lock(_fullVoucherReadback)_fullVoucherReadback.Add(path);
                }
                if(rows.Count==0)return new(false,true,"لم يظهر الحساب الذي أُنشئ بعد.");
                if(rows.Count!=1||!Complete(rows[0]))return new(false,false,"قراءة تأكيد الحساب ناقصة أو غير فريدة.");
                var row=rows[0];
                if(Get(row,".id")!=id||Get(row,nameKey)!=voucher.Username)
                    return new(false,false,"هوية الحساب المقروء لا تطابق الكرت.");
                if(service=="userman")
                {
                    if(!UserManagerV6QuotaCatalog.TryOwner(row,out var owner)||!string.Equals(owner,customer,StringComparison.OrdinalIgnoreCase))
                        return new(false,false,"مالك الحساب غير مؤكد أو لا يطابق حساب المدير.");
                }
                else if(!UserManagerV6Usage.TryBoolean(Get(row,"disabled"),out var disabled)||disabled||
                    service=="pppoe"&&Get(row,"service")!="pppoe")
                    return new(false,false,"تفعيل الحساب أو خدمته غير مؤكد.");
                var actual=Get(row,profileKey);
                if(string.Equals(actual,voucher.Profile,StringComparison.OrdinalIgnoreCase))return new(true,false);
                return new(false,string.IsNullOrWhiteSpace(actual),"حساب الكرت موجود لكن ربط الباقة لم يتأكد.");
            },ct);
            return evidence.Confirmed?null:evidence.Error;
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or InvalidDataException or ObjectDisposedException)
        {return "تعذر تأكيد الحساب وباقته بعد الإضافة؛ لا تعِد الإرسال: "+ex.Message;}
    }

    public async Task<HotspotVoucherDeliveryResult> CreateHotspotVoucherAsync(Voucher voucher,CancellationToken ct=default)
    {
        if(voucher.Service!="hotspot"||string.IsNullOrWhiteSpace(voucher.Profile)||
            voucher.Username.Length is <1 or >64||voucher.Username.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c))||
            voucher.Password is null||voucher.Password.Length>128)throw new ArgumentException("بيانات كرت Hotspot غير صالحة.");
        if(client is not IRouterReceiptClient receipt)throw new NotSupportedException("يلزم إيصال RouterOS لتأكيد إصدار الكرت.");
        var attributes=new Dictionary<string,string>{{"name",voucher.Username},{"profile",voucher.Profile},{"comment",voucher.DeliveryTag}};
        if(voucher.Password.Length>0)attributes["password"]=voucher.Password;
        RouterCommandReply reply;
        try
        {
            using var measure=VoucherPipelineOperation.Measure(VoucherPipelineStage.Add);
            reply=await receipt.CommandWithReceiptAsync("/ip/hotspot/user/add",attributes,ct);
        }
        catch(RouterOsTrapException ex){return new(false,false,null,ex.Message);}
        catch(Exception ex) when(ex is IOException or OperationCanceledException){return new(false,true,null,ex.Message);}
        var id=Get(reply.Done,"ret");
        if(id.Length is <2 or >40||id[0]!='*'||id.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))
            return new(false,true,null,"إيصال الإضافة لا يتضمن معرفا صالحا؛ لا تعِد الإرسال.");
        var error=await VerifyIssuedVoucherAsync(voucher,id,"hotspot","",ct);
        return new(error is null,error is not null,id,error);
    }
}
