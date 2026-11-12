using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    public bool SupportsTaggedSimpleAdd => client is IRouterTaggedAddClient;

    /// <summary>
    /// Independent Hotspot/PPPoE accounts only. Caller holds durable SENDING
    /// leases for the *entire* window. RouterOS may process replies out of
    /// order, so tagged result index is exactly the source voucher index.
    /// User Manager v6 activation deliberately uses the verified sequential
    /// add -> activate -> exact-ID readback path.
    /// </summary>
    public async Task<IReadOnlyList<RouterTaggedAddOutcome>> AddSimpleWindowAsync(
        IReadOnlyList<Voucher> vouchers,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        if(vouchers.Count is < 2 or > 8)
            throw new ArgumentOutOfRangeException(nameof(vouchers));
        if(client is not IRouterTaggedAddClient tagged)
            throw new NotSupportedException("الاتصال لا يدعم إرسال الكروت المجمّع.");
        var service=vouchers[0].Service;
        if(service is not ("hotspot" or "pppoe") ||
            vouchers.Any(v=>v is null||v.Service!=service))
            throw new ArgumentException("لا يمكن جمع خدمات مختلفة أو User Manager في الإرسال المتوازي.");
        var commands=new List<RouterTaggedAddCommand>(vouchers.Count);
        foreach(var voucher in vouchers)
        {
            if(voucher.Username.Length is < 1 or > 64 ||
                voucher.Username.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)) ||
                voucher.Password is null || voucher.Password.Length>128 ||
                string.IsNullOrWhiteSpace(voucher.Profile) || voucher.Profile.Length>100)
                throw new ArgumentException("بيانات كرت غير صالحة لعملية الإرسال المجمّع.");
            Dictionary<string,string> fields;
            string command;
            if(service=="hotspot")
            {
                command="/ip/hotspot/user/add";
                fields=new Dictionary<string,string>
                {
                    ["name"]=voucher.Username,
                    ["profile"]=voucher.Profile,
                    ["comment"]="NajmCard batch "+voucher.BatchId
                };
                // Preserve Hadramout's username-only semantics exactly:
                // no password property means the card is username-only.
                if(voucher.Password.Length>0)fields["password"]=voucher.Password;
            }
            else
            {
                command="/ppp/secret/add";
                fields=new Dictionary<string,string>
                {
                    ["name"]=voucher.Username,
                    ["password"]=voucher.Password,
                    ["profile"]=voucher.Profile,
                    ["service"]="pppoe"
                };
            }
            RouterOsCommandPolicy.Validate(command,fields);
            commands.Add(new RouterTaggedAddCommand(command,fields));
        }
        return await tagged.AddWindowAsync(commands,ct);
    }
}
