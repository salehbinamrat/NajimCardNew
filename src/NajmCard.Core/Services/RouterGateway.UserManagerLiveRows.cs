using System.Globalization;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// User Manager v6/v7 live snapshots for the six-screen UI. All reads are
/// password-free projected RouterOS API requests. No synthetic totals, no writes,
/// no automatic repeated polling and no per-account N+1 router queries.
/// </summary>
public sealed partial class RouterGateway
{
    private static string UmValue(IReadOnlyDictionary<string,string> row,string key)
        => row.TryGetValue(key,out var value)?value.Trim():"";

    private static string UmKnown(IReadOnlyDictionary<string,string> row,string key)
        => row.TryGetValue(key,out var value)&&!string.IsNullOrWhiteSpace(value)
            ?value.Trim():"غير متاح";

    private static long? UmBytes(IReadOnlyDictionary<string,string> row,string key)
    {
        if(!row.TryGetValue(key,out var value) || string.IsNullOrWhiteSpace(value))
            return null;
        var input=value.Trim();
        if(long.TryParse(input,NumberStyles.Integer,CultureInfo.InvariantCulture,out var result)
            && result>=0)return result;
        // RouterOS can send human readable units for legacy counters.
        var parsed=UserManagerV6Usage.ParseBytes(input);
        return parsed>0 ? parsed : null;
    }

    private static bool? UmBoolean(IReadOnlyDictionary<string,string> row,string key)
        => UmValue(row,key).ToLowerInvariant() switch
        {
            "yes" or "true" or "1"=>true,
            "no" or "false" or "0"=>false,
            _=>null
        };

    /// <summary>
    /// Router local calendar time: keep unspecified time zone rather than
    /// silently applying the Windows host timezone to reported sessions.
    /// </summary>
    public static DateTime? ParseUmDate(string raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return null;
        var formats=new[]
        {
            "yyyy-MM-dd HH:mm:ss","yyyy-MM-dd HH:mm",
            "yyyy-MM-ddTHH:mm:ss","MMM/dd/yyyy HH:mm:ss",
            "MMM/dd/yyyy HH:mm","MMM/dd/yyyy","yyyy-MM-dd",
            "MMM/dd/yyyy h:mm:ss tt",
            "MMM/d/yyyy HH:mm:ss","MMM/d/yyyy HH:mm","MMM/d/yyyy",
            "MMM/d/yyyy h:mm:ss tt","M/d/yyyy HH:mm:ss",
            "M/d/yyyy h:mm:ss tt","M/d/yyyy"
        };
        return DateTime.TryParseExact(raw,formats,CultureInfo.InvariantCulture,
            DateTimeStyles.None,out var timestamp)
            ?DateTime.SpecifyKind(timestamp,DateTimeKind.Unspecified):null;
    }

    public async Task<UmDataSnapshot<UmAccountRow>> ReadUmAccountsAsync(
        CancellationToken ct=default)
    {
        var version=await ReadRouterOsGenerationAsync(ct);
        if(version==RouterOsGeneration.V6)
        {
            // Reuse the existing transfer-limit-only lifecycle classifier.
            // A started voucher is NOT automatically a currently connected session.
            var inventory=await ReadUserManagerV6InventoryAsync(ct);
            var accounts=inventory.Users.Select(item=>new UmAccountRow(
                item.Id,item.Username,item.Profile.Length==0?"غير متاح":item.Profile,
                item.Status switch
                {
                    "active"=>"استخدم",
                    "valid"=>"صالح",
                    "expired"=>"منتهي",
                    "disabled"=>"معطل",
                    "incomplete"=>"غير مكتمل",
                    _=>"غير مصنف"
                },
                item.Disabled,
                "غير متاح",
                inventory.IsComplete?item.DownloadBytes:null,
                inventory.IsComplete?item.UploadBytes:null,
                item.TransferLimit>0?item.TransferLimit:null,
                inventory.IsComplete?item.ActiveSessions:null,
                string.IsNullOrWhiteSpace(item.LastSeen)?"غير متاح":item.LastSeen,
                inventory.Diagnostic.Length>0?inventory.Diagnostic:item.Reason)).ToArray();
            return new UmDataSnapshot<UmAccountRow>(version,accounts);
        }

        // v7 profiles are stored in a SEPARATE user-profile table. One projected
        // bulk query is cheaper and more accurate than per-user profile reads.
        var users=await client.CommandAsync("/user-manager/user/print",
            new Dictionary<string,string>
            {
                [".proplist"]=".id,name,disabled,shared-users"
            },ct);
        if(users.Any(row=>!row.ContainsKey(".id") || !row.ContainsKey("name") ||
            !row.ContainsKey("disabled")))
            throw new InvalidDataException(
                "أعاد User Manager 7 صفوف مستخدمين دون معرف أو اسم أو حالة تعطيل.");

        var assigned=await client.CommandAsync("/user-manager/user-profile/print",
            new Dictionary<string,string>
            {
                [".proplist"]=".id,user,profile,state,end-time"
            },ct);
        if(assigned.Any(row=>!row.ContainsKey("user") || !row.ContainsKey("profile")))
            throw new InvalidDataException(
                "تعذرت قراءة إسنادات الباقات في User Manager 7 بصورة موثوقة.");
        var links=assigned.GroupBy(row=>UmValue(row,"user"),
            StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,
            group=>group.ToArray(),StringComparer.OrdinalIgnoreCase);
        var output=new List<UmAccountRow>(users.Count);
        foreach(var row in users)
        {
            var name=UmValue(row,"name");
            var disabled=UmBoolean(row,"disabled");
            links.TryGetValue(name,out var profiles);
            var selected=profiles?.FirstOrDefault(x=>UmValue(x,"state").Contains(
                "running active",StringComparison.OrdinalIgnoreCase))
                ??profiles?.FirstOrDefault(x=>string.Equals(UmValue(x,"state"),"running",
                    StringComparison.OrdinalIgnoreCase))
                ??profiles?.FirstOrDefault();
            var profile=selected is null?"غير متاح":UmKnown(selected,"profile");
            var state=selected is null?"":UmValue(selected,"state");
            var status=disabled switch
            {
                true=>"معطل",
                null=>"غير معروف",
                false when selected is null=>"بدون باقة",
                false when state.Equals("used",StringComparison.OrdinalIgnoreCase)
                    && profiles is not null && profiles.All(x=>UmValue(x,"state")
                    .Equals("used",StringComparison.OrdinalIgnoreCase))=>"منتهي",
                false when state.Contains("running active",StringComparison.OrdinalIgnoreCase)
                    =>"باقة نشطة",
                false when state=="running"=>"باقة جاهزة",
                false=>"مفعل"
            };
            output.Add(new UmAccountRow(UmValue(row,".id"),name,profile,status,
                disabled,selected is null?"غير متاح":UmKnown(selected,"end-time"),
                null,null,null,null,"غير متاح",
                "تتطلب إحصاءات مستخدم v7 التفصيلية قراءة monitor لحساب محدد."));
        }
        return new UmDataSnapshot<UmAccountRow>(version,output);
    }

    public async Task<UmDataSnapshot<UmPlanRow>> ReadUmPlansAsync(
        CancellationToken ct=default)
    {
        var version=await ReadRouterOsGenerationAsync(ct);
        var v7=version==RouterOsGeneration.V7;
        var profiles=await client.CommandAsync(v7?"/user-manager/profile/print":
            "/tool/user-manager/profile/print",
            new Dictionary<string,string> { [".proplist"]=v7
                ?".id,name,price,validity,starts-when"
                :".id,name,owner,price,validity,starts-at" },ct);
        var limitations=await client.CommandAsync(v7?"/user-manager/limitation/print":
            "/tool/user-manager/profile/limitation/print",
            new Dictionary<string,string> { [".proplist"]=v7
                ?".id,name,transfer-limit,download-limit,upload-limit,uptime-limit,rate-limit-rx,rate-limit-tx"
                :".id,name,owner,transfer-limit,download-limit,upload-limit,uptime-limit,rate-limit-rx,rate-limit-tx" },ct);
        var relations=await client.CommandAsync(v7?"/user-manager/profile-limitation/print":
            "/tool/user-manager/profile/profile-limitation/print",
            new Dictionary<string,string> { [".proplist"]="profile,limitation" },ct);
        if(profiles.Any(row=>!row.ContainsKey(".id")||!row.ContainsKey("name")) ||
           limitations.Any(row=>!row.ContainsKey(".id")||!row.ContainsKey("name")))
            throw new InvalidDataException(
                "تفتقد بعض الباقات أو القيود معرفات صحيحة؛ لا يمكن عرض قائمة مكتملة.");
        // Real v6 routers can retain orphaned profile-limitation links after
        // a profile/limitation was removed. Keep all verified profile rows
        // visible while clearly flagging these unresolved associations.
        // Never invent the missing limit or claim a complete association.
        int unresolved=relations.Count(row=>!row.ContainsKey("profile")||
            !row.ContainsKey("limitation"));

        var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in limitations)
        {
            names[UmValue(r,".id")]=UmValue(r,"name");
            names[UmValue(r,"name")]=UmValue(r,"name");
        }
        var profileAliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in profiles)
        {
            profileAliases[UmValue(r,".id")]=UmValue(r,"name");
            profileAliases[UmValue(r,"name")]=UmValue(r,"name");
        }
        var grouped=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var r in relations)
        {
            if(!profileAliases.TryGetValue(UmValue(r,"profile"),out var profile) ||
                !names.TryGetValue(UmValue(r,"limitation"),out var limitation))
            {
                unresolved++;
                continue;
            }
            if(!grouped.TryGetValue(profile,out var set))
                grouped[profile]=set=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(limitation);
        }

        var rows=new List<UmPlanRow>(profiles.Count+limitations.Count);
        foreach(var row in profiles)
        {
            var name=UmValue(row,"name");
            var linked=grouped.TryGetValue(name,out var groups)
                ?string.Join("، ",groups.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
                :unresolved>0?"غير مؤكد (توجد روابط ناقصة)":"غير مرتبط";
            rows.Add(new UmPlanRow(
                UmValue(row,".id"),"Profile",name,UmKnown(row,"price"),
                UmKnown(row,"validity"),UmKnown(row,v7?"starts-when":"starts-at"),
                "غير متاح","غير متاح","غير متاح","غير متاح",
                "غير متاح","غير متاح",linked)
            { Owner=v7?"":UmValue(row,"owner") });
        }
        foreach(var row in limitations)
        {
            rows.Add(new UmPlanRow(
                UmValue(row,".id"),"Limitation",UmValue(row,"name"),
                "غير متاح","غير متاح","غير متاح",
                UmKnown(row,"transfer-limit"),UmKnown(row,"download-limit"),
                UmKnown(row,"upload-limit"),UmKnown(row,"uptime-limit"),
                UmKnown(row,"rate-limit-rx"),UmKnown(row,"rate-limit-tx"),
                string.Join("، ",grouped.Where(x=>x.Value.Contains(UmValue(row,"name")))
                    .Select(x=>x.Key).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)))
            { Owner=v7?"":UmValue(row,"owner") });
        }
        return new UmDataSnapshot<UmPlanRow>(version,rows)
        {
            IsComplete=unresolved==0,
            Diagnostic=unresolved==0?"":
                $"تم عرض الباقات والقيود المقروءة، لكن {unresolved} رابطا "+
                "بين Profile وLimitation غير قابل للتحقق. "+
                "لا تعتمد حدود تلك الروابط قبل مراجعة User Manager على الراوتر."
        };
    }

    private static string UmSessionField(
        IReadOnlyDictionary<string,string> row,params string[] aliases)
    {
        foreach(var alias in aliases)
        {
            var value=UmValue(row,alias);
            if(value.Length>0)return value;
        }
        return "غير متاح";
    }

    private static UmSessionRow[] ParseUmSessionRows(
        IReadOnlyList<IReadOnlyDictionary<string,string>> rows,
        RouterOsGeneration version)
    {
        var v7=version==RouterOsGeneration.V7;
        if(rows.Any(row=>!row.ContainsKey(".id") ||
            string.IsNullOrWhiteSpace(UmValue(row,".id"))) ||
            rows.Select(row=>UmValue(row,".id"))
                .Distinct(StringComparer.Ordinal).Count()!=rows.Count)
            throw new InvalidDataException(
                "سجلات User Manager لا تحتوي معرفات فريدة مؤكدة؛ "+
                "لا يمكن حساب العدد الحقيقي.");
        var result=new List<UmSessionRow>(rows.Count);
        foreach(var row in rows)
        {
            var user=UmSessionField(row,"username","user","user-name","name");
            if(user=="غير متاح")
                throw new InvalidDataException(
                    "وجدت جلسة دون معرف مستخدم في قراءة User Manager؛ "+
                    "لم يعتمد عدد جزئي أو صفري مضلل.");
            var started=UmSessionField(row,v7?"started":"from-time",
                "started","start-time");
            var ended=UmSessionField(row,v7?"ended":"to-time",
                "ended","till-time","end-time");
            var active=UmBoolean(row,"active");
            // Do not derive online state from a missing end timestamp.
            var rawStatus=UmSessionField(row,"status","session-status");
            result.Add(new UmSessionRow(
                UmValue(row,".id"),user,started,ended,
                UmSessionField(row,"uptime","session-time"),
                UmBytes(row,"download"),UmBytes(row,"upload"),
                UmSessionField(row,v7?"user-address":"user-ip",
                    "user-address","ip-address","framed-ip-address","address"),
                UmSessionField(row,v7?"calling-station-id":"calling-station-id",
                    "mac-address","caller-id"),
                active,ParseUmDate(started),rawStatus));
        }
        return result.ToArray();
    }

    public async Task<UmDataSnapshot<UmSessionRow>> ReadUmSessionsAsync(
        CancellationToken ct=default)
    {
        var version=await ReadRouterOsGenerationAsync(ct);
        var v7=version==RouterOsGeneration.V7;
        var menu=v7?"/user-manager/session/print":"/tool/user-manager/session/print";
        var properties=v7
            ?".id,user,active,started,ended,uptime,download,upload,user-address,calling-station-id,status"
            // RouterOS v6 has several equivalent session field spellings.
            // Include the legacy UI's user-ip and calling-station-id rather
            // than silently displaying 'not available' for populated rows.
            :".id,user,username,user-name,name,active,from-time,to-time,till-time,started,ended,uptime,download,upload,user-ip,ip-address,user-address,framed-ip-address,calling-station-id,mac-address,caller-id,status";
        var rows=await client.CommandAsync(menu,
            new Dictionary<string,string>{{".proplist",properties}},ct);
        try
        {
            return new UmDataSnapshot<UmSessionRow>(version,
                ParseUmSessionRows(rows,version));
        }
        catch(InvalidDataException) when(!v7)
        {
            // Some v6 builds omit the username when the API's projection
            // differs from the old web User Manager table. Retry read-only
            // without a projection; never synthesize a missing session.
            var full=await client.CommandAsync(menu,null,ct);
            return new UmDataSnapshot<UmSessionRow>(version,
                ParseUmSessionRows(full,version))
            {
                Diagnostic="أعيدت القراءة بجميع حقول جلسات RouterOS 6."
            };
        }
    }

    /// <summary>
    /// Return only sessions that belong to the verified account name or its
    /// RouterOS internal ID. If none are found in the projected v6 list,
    /// re-read unprojected v6 sessions because old UM web versions expose
    /// different column names. All matches are exact, never prefix matches.
    /// </summary>
    public async Task<UmDataSnapshot<UmSessionRow>> ReadUmSessionsForAccountAsync(
        string username,string? internalId=null,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("اسم الحساب مطلوب.",nameof(username));
        var current=await ReadUmSessionsAsync(ct);
        bool Matches(UmSessionRow row)=>
            string.Equals(row.Name,username,StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(internalId) &&
             string.Equals(row.Name,internalId,StringComparison.Ordinal));
        UmSessionRow[] Selected(IEnumerable<UmSessionRow> list)=>
            list.Where(Matches)
                .Select(row=>row with{Name=username})
                .ToArray();
        var selected=Selected(current.Rows);
        if(current.Generation!=RouterOsGeneration.V6 || selected.Length>0)
            return current with{Rows=selected};
        var raw=await client.CommandAsync("/tool/user-manager/session/print",null,ct);
        var full=ParseUmSessionRows(raw,RouterOsGeneration.V6);
        return new UmDataSnapshot<UmSessionRow>(RouterOsGeneration.V6,Selected(full))
        {
            Diagnostic="فحص إضافي لكامل حقول جلسات User Manager v6 "+
                "عند عدم وجود تطابق في القراءة السريعة."
        };
    }

}
