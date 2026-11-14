using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

internal sealed record UserManagerV6ResolvedQuota(
    string ProfileId,string Owner,long Bytes,bool IsVerified,string Diagnostic)
{
    internal static UserManagerV6ResolvedQuota Unknown(string diagnostic)=>
        new("","",0,false,diagnostic);
}

/// <summary>
/// v6 names are scoped to a customer. IDs never collapse into bare names.
/// Unknown ownership, invalid values and unresolved relations are not zero limits.
/// This is a read snapshot, never a cache or authority to mutate RouterOS.
/// </summary>
internal sealed class UserManagerV6QuotaCatalog
{
    private sealed record Definition(string Id,string Name,string Owner,long Bytes=0);
    private readonly List<Definition> _profiles=[];
    private readonly Dictionary<string,Definition> _profilesById=new(StringComparer.Ordinal);
    private readonly Dictionary<string,List<Definition>> _profilesByName=new(StringComparer.Ordinal);
    private readonly Dictionary<string,List<Definition>> _linked=new(StringComparer.Ordinal);
    private readonly Dictionary<string,HashSet<string>> _reverseLinks=new(StringComparer.Ordinal);
    private readonly List<string> _errors=[];
    internal bool IsComplete=>_errors.Count==0;
    internal string Diagnostic=>string.Join(" ",_errors.Distinct().Take(5));

    private static string Value(IReadOnlyDictionary<string,string> row,string key)=>
        row.TryGetValue(key,out var value)?value.Trim():"";

    internal static bool TryOwner(IReadOnlyDictionary<string,string> row,out string owner)
    {
        var customer=Value(row,"customer");
        owner=Value(row,"owner");
        if(owner.Length>0&&customer.Length>0&&
            !string.Equals(owner,customer,StringComparison.Ordinal))return false;
        if(owner.Length==0)owner=customer;
        return owner.Length>0;
    }

    internal UserManagerV6QuotaCatalog(
        IReadOnlyList<IReadOnlyDictionary<string,string>> profiles,
        IReadOnlyList<IReadOnlyDictionary<string,string>> limitations,
        IReadOnlyList<IReadOnlyDictionary<string,string>> relations)
    {
        var limitsById=new Dictionary<string,Definition>(StringComparer.Ordinal);
        var limitsByName=new Dictionary<string,List<Definition>>(StringComparer.Ordinal);
        void Add(IReadOnlyDictionary<string,string> row,bool limitation)
        {
            var id=Value(row,".id");var name=Value(row,"name");
            bool ownerKnown=TryOwner(row,out var owner);
            long bytes=0;
            if(id.Length==0||name.Length==0||!ownerKnown ||
                (limitation&&!UserManagerV6Usage.TryParseQuota(Value(row,"transfer-limit"),out bytes)))
            {
                _errors.Add("تعذر التحقق من هوية أو مالك أو حدود تعريف باقة User Manager.");
                return;
            }
            var item=new Definition(id,name,owner,bytes);
            var byId=limitation?limitsById:_profilesById;
            var byName=limitation?limitsByName:_profilesByName;
            if(!byId.TryAdd(id,item))
            {
                _errors.Add("معرف تعريف باقة User Manager مكرر.");
                return;
            }
            if(!byName.TryGetValue(name,out var matches))byName[name]=matches=[];
            if(matches.Any(x=>string.Equals(x.Owner,owner,StringComparison.Ordinal)))
                _errors.Add("اسم تعريف باقة مكرر داخل المالك نفسه.");
            matches.Add(item);
            if(!limitation){_profiles.Add(item);_linked[id]=[];}
        }
        foreach(var row in profiles)Add(row,false);
        foreach(var row in limitations)Add(row,true);
        foreach(var link in relations)
        {
            // A relation may omit owner if its profile ID (or unique name)
            // determines it. A conflicting explicit owner must never be ignored.
            bool hasOwner=Value(link,"owner").Length>0||Value(link,"customer").Length>0;
            bool ownerKnown=TryOwner(link,out var owner);
            var profile=hasOwner&&!ownerKnown?null:
                Resolve(Value(link,"profile"),owner,_profilesById,_profilesByName);
            var limit=profile is null?null:
                Resolve(Value(link,"limitation"),profile.Owner,limitsById,limitsByName);
            if(profile is null||limit is null)
            {
                _errors.Add("رابط باقة User Manager مفقود أو ملتبس أو يخص مالكا آخر؛ الحدود غير مؤكدة.");
                continue;
            }
            _linked[profile.Id].Add(limit);
            if(!_reverseLinks.TryGetValue(limit.Id,out var profileNames))
                _reverseLinks[limit.Id]=profileNames=new(StringComparer.Ordinal);
            profileNames.Add(profile.Name);
        }
    }

    private static Definition? Resolve(string reference,string owner,
        Dictionary<string,Definition> byId,Dictionary<string,List<Definition>> byName)
    {
        if(byId.TryGetValue(reference,out var idMatch))
            return owner.Length==0||string.Equals(idMatch.Owner,owner,StringComparison.Ordinal)
                ?idMatch:null;
        if(reference.StartsWith('*')||!byName.TryGetValue(reference,out var matches))return null;
        var selected=matches.Where(x=>owner.Length==0||
            string.Equals(x.Owner,owner,StringComparison.Ordinal)).Take(2).ToArray();
        return selected.Length==1?selected[0]:null;
    }

    internal UserManagerV6ResolvedQuota ForUser(IReadOnlyDictionary<string,string> user)
    {
        if(!IsComplete)return UserManagerV6ResolvedQuota.Unknown(Diagnostic);
        if(!TryOwner(user,out var owner))
            return UserManagerV6ResolvedQuota.Unknown("مالك حساب User Manager غير مؤكد.");
        var profile=Resolve(Value(user,"actual-profile"),owner,_profilesById,_profilesByName);
        if(profile is null)
            return UserManagerV6ResolvedQuota.Unknown("هوية باقة الحساب وملكيتها غير مؤكدتين.");
        var bytes=EffectiveLimit(profile.Id);
        // v6 may omit an unset per-user transfer-limit. A present malformed
        // value must not be silently substituted with a profile limit.
        var direct=Value(user,"transfer-limit");
        if(direct.Length>0)
        {
            if(!UserManagerV6Usage.TryParseQuota(direct,out var parsed))
                return UserManagerV6ResolvedQuota.Unknown("حد نقل بيانات الحساب غير صالح.");
            if(parsed>0)bytes=parsed;
        }
        return new(profile.Id,profile.Owner,bytes,true,"");
    }

    private long EffectiveLimit(string id)=>_linked[id].Select(x=>x.Bytes)
        .Where(x=>x>0).DefaultIfEmpty(0).Min();

    internal string LimitationNamesFor(string profileId)=>_linked.TryGetValue(profileId,out var limits)
        &&limits.Count>0?string.Join("، ",limits.Select(x=>x.Name).Distinct()
            .OrderBy(x=>x,StringComparer.Ordinal)):
            IsComplete?"غير مرتبط":"غير مؤكد (توجد روابط ناقصة)";

    internal string ProfileNamesFor(string limitationId)=>
        _reverseLinks.TryGetValue(limitationId,out var profiles)
            ?string.Join("، ",profiles.OrderBy(x=>x,StringComparer.Ordinal)):"";

    internal IReadOnlyList<UserManagerV6ProfileLimitSummary> Summaries()
    {
        if(!IsComplete)throw new InvalidDataException(Diagnostic);
        return _profiles.Select(p=>new UserManagerV6ProfileLimitSummary(p.Id,p.Name,
            EffectiveLimit(p.Id),_linked[p.Id].Count,
            string.Join("، ",_linked[p.Id].Select(x=>x.Name))) {Owner=p.Owner})
            .OrderBy(x=>x.ProfileName,StringComparer.Ordinal)
            .ThenBy(x=>x.Owner,StringComparer.Ordinal).ToArray();
    }
}
