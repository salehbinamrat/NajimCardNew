using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    private readonly RouterReadCache<UmDataSnapshot<UmSessionRow>> _sessionReads=new(TimeSpan.FromSeconds(20));
    private bool _sessionProjectionUnsupported,_sessionFilterUnsupported;
    private const string SessionProjection=
        ".id,user,username,user-name,name,active,from-time,to-time,till-time,started,ended,start-time,end-time,"+
        "uptime,session-time,download,upload,user-ip,ip-address,user-address,framed-ip-address,address,"+
        "calling-station-id,mac-address,caller-id,status,session-status";

    private static bool UnsupportedProjection(RouterOsTrapException ex)=>
        ex.Message.Contains("unknown parameter",StringComparison.OrdinalIgnoreCase)||
        ex.Message.Contains("invalid argument",StringComparison.OrdinalIgnoreCase)||
        ex.Message.Contains("no such argument",StringComparison.OrdinalIgnoreCase);
    private static bool UnsupportedSessionQuery(RouterOsTrapException ex)=>
        ex.Message.Contains("query not supported",StringComparison.OrdinalIgnoreCase)||
        ex.Message.Contains("unknown query",StringComparison.OrdinalIgnoreCase)||
        ex.Message.Contains("invalid query",StringComparison.OrdinalIgnoreCase);

    private async Task<UmDataSnapshot<UmSessionRow>> ReadSessionRowsAsync(RouterOsGeneration version,
        Func<IReadOnlyDictionary<string,string>?,Task<IReadOnlyList<IReadOnlyDictionary<string,string>>>> read)
    {
        bool full=_sessionProjectionUnsupported;
        IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
        try{rows=await read(full?null:new Dictionary<string,string>{{".proplist",SessionProjection}});}
        catch(RouterOsTrapException ex) when(!full&&UnsupportedProjection(ex))
        {full=true;rows=await read(null);}
        UmSessionRow[] parsed;
        try{parsed=ParseUmSessionRows(rows,version);}
        catch(InvalidDataException) when(!full&&version==RouterOsGeneration.V6)
        {full=true;rows=await read(null);parsed=ParseUmSessionRows(rows,version);}
        // Empty is a valid result. Missing times/counters remain unknown; they
        // cannot justify repeatedly downloading an unprojected history table.
        if(full)_sessionProjectionUnsupported=true;
        return new UmDataSnapshot<UmSessionRow>(version,parsed);
    }

    public async Task<UmDataSnapshot<UmSessionRow>> ReadUmSessionsAsync(
        CancellationToken ct=default,bool force=true)
    {
        var read=await _sessionReads.ReadAsync(async token=>
        {
            using var readLease=await EnterHeavyReadAsync(token);
            var version=await ReadCachedRouterOsGenerationAsync(ct:token);
            var menu=version==RouterOsGeneration.V7?"/user-manager/session/print":"/tool/user-manager/session/print";
            return await ReadSessionRowsAsync(version,attrs=>client.CommandAsync(menu,attrs,token));
        },x=>x.IsComplete,force,ct);
        return read.Value with {ReadAt=read.ReadAt,IsFromCache=read.FromCache,
            IsComplete=read.Value.IsComplete&&!read.Invalidated,
            Diagnostic=read.Invalidated?"تغيرت الجلسات أثناء القراءة؛ حدّث القائمة.":read.Value.Diagnostic};
    }

    public async Task<UmDataSnapshot<UmSessionRow>> ReadUmSessionsForAccountAsync(
        string username,string? internalId=null,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(username))throw new ArgumentException("اسم الحساب مطلوب.",nameof(username));
        if(string.IsNullOrEmpty(internalId))internalId=null;
        if(internalId is not null)ValidateInternalId(internalId);
        bool Matches(UmSessionRow row)=>row.Name==username||internalId is not null&&row.Name==internalId;
        if(!_sessionFilterUnsupported&&client is IRouterSessionQueryClient query)
        {
            var version=await ReadCachedRouterOsGenerationAsync(ct:ct);
            var menu=version==RouterOsGeneration.V7?"/user-manager/session/print":"/tool/user-manager/session/print";
            try
            {
                using var readLease=await EnterHeavyReadAsync(ct);
                long revision=_sessionReads.Revision;
                var result=await ReadSessionRowsAsync(version,attrs=>
                    query.QuerySessionsForAccountAsync(menu,username,internalId,attrs,ct));
                bool stable=_sessionReads.IsStable(revision);
                if(result.Rows.All(Matches))return result with
                {
                    Rows=result.Rows.Select(row=>row with{Name=username}).ToArray(),
                    IsComplete=stable,
                    Diagnostic=stable?"":"تغيرت الجلسات أثناء القراءة؛ حدّث القائمة."
                };
                // Some old builds ignore filters. Detect unrelated rows, then
                // share ONE bounded-lifetime full snapshot for account selection.
                _sessionFilterUnsupported=true;
            }
            catch(NotSupportedException){_sessionFilterUnsupported=true;}
            catch(RouterOsTrapException ex) when(UnsupportedSessionQuery(ex)){_sessionFilterUnsupported=true;}
        }
        var current=await ReadUmSessionsAsync(ct,force:false);
        return current with {Rows=current.Rows.Where(Matches).Select(row=>row with{Name=username}).ToArray()};
    }
}
