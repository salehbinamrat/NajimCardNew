using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Read-only User Manager explorer. v6 and v7 have DIFFERENT menu paths and
/// account/profile data models; this method never mutates router configuration
/// or performs voucher issuance.
/// </summary>
public sealed partial class RouterGateway
{
    public async Task<RouterOsGeneration> ReadRouterOsGenerationAsync(
        CancellationToken ct = default)
    {
        var rows=await client.CommandAsync("/system/resource/print",cancellationToken:ct);
        if(rows.Count!=1)
            throw new InvalidDataException("تعذر التحقق من إصدار RouterOS؛ أُوقفت قراءة User Manager.");
        var version=Get(rows[0],"version").Trim();
        var major=new string(version.TakeWhile(char.IsAsciiDigit).ToArray());
        if(!int.TryParse(major,out var number) || number is not (6 or 7))
            throw new NotSupportedException(
                "إصدار RouterOS غير معتمد لقسم User Manager: "+(version.Length==0?"غير معروف":version));
        return number==6?RouterOsGeneration.V6:RouterOsGeneration.V7;
    }

    public async Task<UserManagerCatalogSnapshot> ReadUserManagerCatalogAsync(
        UserManagerCatalogSection section,CancellationToken ct=default)
    {
        if(!Enum.IsDefined(section))
            throw new ArgumentOutOfRangeException(nameof(section));
        var generation=await ReadRouterOsGenerationAsync(ct);
        bool v7=generation==RouterOsGeneration.V7;
        var prefix=v7?"/user-manager":"/tool/user-manager";
        var (menu,properties)=section switch
        {
            UserManagerCatalogSection.Users=>(
                prefix+"/user/print",v7
                    ?".id,name,disabled,shared-users"
                    :".id,username,disabled,actual-profile,uptime-used,total-download-used,total-upload-used"),
            UserManagerCatalogSection.Profiles=>(
                prefix+"/profile/print",v7
                    ?".id,name,validity,price,starts-when"
                    :".id,name,validity,price,starts-at"),
            UserManagerCatalogSection.Limitations=>(
                v7?prefix+"/limitation/print":prefix+"/profile/limitation/print",
                v7?".id,name,transfer-limit,download-limit,upload-limit,uptime-limit"
                  :".id,name,transfer-limit,uptime-limit"),
            UserManagerCatalogSection.Sessions=>(
                prefix+"/session/print",v7
                    ?".id,user,uptime,download,upload,user-address,calling-station-id,active"
                    :".id,user,uptime,download,upload"),
            _=>throw new ArgumentOutOfRangeException(nameof(section))
        };
        // A projected read is important: some UM user tables contain passwords.
        // Fail closed on malformed/missing projections; never fall back to a
        // full sensitive record scan just to fill a display field.
        var rows=await client.CommandAsync(menu,
            new Dictionary<string,string>{{".proplist",properties}},ct);
        var output=new List<UserManagerCatalogRow>(rows.Count);
        foreach(var row in rows)
        {
            string Field(params string[] keys)
            {
                foreach(var key in keys)
                {
                    var value=Get(row,key).Trim();
                    if(value.Length>0)return value;
                }
                return "غير معروف";
            }
            // Detect an unsupported projection/partial response rather than
            // painting a false empty account name.
            if(!row.ContainsKey(".id") ||
                !row.ContainsKey(section==UserManagerCatalogSection.Users&&!v7
                    ?"username":section==UserManagerCatalogSection.Sessions?"user":"name"))
                throw new InvalidDataException(
                    "أعاد RouterOS سجلا ناقصا؛ لا يمكن اعتماد نتائج User Manager.");
            var name=section switch
            {
                UserManagerCatalogSection.Users=>v7?Field("name"):Field("username"),
                UserManagerCatalogSection.Sessions=>Field("user"),
                _=>Field("name")
            };
            var profile=section==UserManagerCatalogSection.Users&&!v7
                ?Field("actual-profile"):"غير متاح هنا";
            var status=section==UserManagerCatalogSection.Users
                ?Field("disabled") switch
                  {
                      "yes" or "true" => "معطل",
                      "no" or "false" => "مفعل",
                      _ => "غير معروف"
                  }
                :section==UserManagerCatalogSection.Sessions?Field("active"):"—";
            var limit=section==UserManagerCatalogSection.Limitations
                ?Field("transfer-limit","download-limit"):"—";
            var extra=section switch
            {
                UserManagerCatalogSection.Profiles=>Field(v7?"starts-when":"starts-at"),
                UserManagerCatalogSection.Users=>v7?Field("shared-users"):Field("actual-profile"),
                UserManagerCatalogSection.Limitations=>Field("uptime-limit"),
                UserManagerCatalogSection.Sessions=>Field("calling-station-id"),
                _=>"—"
            };
            output.Add(new UserManagerCatalogRow(
                Field(".id"),name,profile,status,limit,
                section==UserManagerCatalogSection.Profiles?Field("validity"):
                section==UserManagerCatalogSection.Limitations?Field("uptime-limit"):
                section==UserManagerCatalogSection.Users&&!v7?Field("uptime-used"):
                Field("uptime"),
                section==UserManagerCatalogSection.Users&&!v7
                    ?Field("total-download-used"):Field("download"),
                section==UserManagerCatalogSection.Users&&!v7
                    ?Field("total-upload-used"):Field("upload"),
                section==UserManagerCatalogSection.Sessions?Field("user-address"):"—",
                extra));
        }
        return new UserManagerCatalogSnapshot(generation,section,output);
    }
}
