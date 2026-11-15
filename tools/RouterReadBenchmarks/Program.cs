using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NajmCard.Core.Services;

// No sockets, credentials, writes or real router. Same deterministic fixture/workload
// before/after; wall time is local CPU time, the network column is a stated model.
foreach(var scenario in new[]{"account-sessions","hotspot-users","ppp-inventory","shared-um-inventory"})
foreach(var count in new[]{1,100,1000,10000})
{
    var samples=new List<double>();Fixture? measured=null;int returned=0;
    for(var repeat=0;repeat<6;repeat++)
    {
        var router=new Fixture(count);var gateway=new RouterGateway(router);
        var timer=Stopwatch.StartNew();int rows=0;
        switch(scenario)
        {
            case "account-sessions":
                foreach(var name in new[]{"card","absent","card","absent"})
                    rows+=(await gateway.ReadUmSessionsForAccountAsync(name,"*F")).Rows.Count;
                break;
            case "hotspot-users":rows=(await gateway.ReadUsersAsync()).Count;break;
            case "ppp-inventory":rows=(await gateway.ReadPppInventoryAsync()).Accounts.Count;break;
            case "shared-um-inventory":
                var accounts=await gateway.ReadUmAccountsAsync();
                var dashboard=await gateway.ReadCachedUserManagerV6InventoryAsync();
                if(!accounts.IsComplete||!dashboard.IsComplete)throw new InvalidOperationException("Incomplete fixture");
                rows=accounts.Rows.Count+dashboard.Total;break;
        }
        timer.Stop();if(repeat>0)samples.Add(timer.Elapsed.TotalMilliseconds);
        measured=router;returned=rows;
    }
    var expected=scenario=="account-sessions"?2:scenario=="shared-um-inventory"?2*count:count;
    if(returned!=expected)throw new InvalidOperationException("Lost rows: "+scenario);
#if STAGE3
    var maxRequests=scenario is "account-sessions" or "shared-um-inventory"?5:scenario=="ppp-inventory"?3:1;
    if(measured!.Requests>maxRequests || scenario=="account-sessions"&&measured.Bytes>1024 ||
        scenario is "hotspot-users" or "ppp-inventory" && measured.Bytes>count*600)
        throw new InvalidOperationException("API budget regression: "+scenario);
#endif
    samples.Sort();
    Console.WriteLine(JsonSerializer.Serialize(new{scenario,rows=count,requests=measured!.Requests,
        response_bytes=measured.Bytes,returned_rows=returned,local_median_ms=Math.Round(samples[2],3),
        modeled_network_ms=Math.Round(measured.Requests*5+measured.Bytes*1000.0/(1024*1024),3),
        errors=0,retries=0}));
}

sealed class Fixture : IRouterCommandClient
#if STAGE3
    ,IRouterSessionQueryClient
#endif
{
    private readonly IReadOnlyDictionary<string,string>[] rows;
    public int Requests;public long Bytes;
    public Fixture(int count)=>rows=Enumerable.Range(0,count).Select(i=>
        (IReadOnlyDictionary<string,string>)new Dictionary<string,string>{[".id"]="*"+(i+1).ToString("X"),
            ["username"]=i==0?"card":"unrelated"+i,["name"]=i==0?"card":"unrelated"+i,
            ["profile"]="gold",["actual-profile"]="gold",["customer"]="alice",["disabled"]="no",["incomplete"]="no",
            ["download-used"]="0",["upload-used"]="0",["password"]="synthetic-only",["comment"]="fixture",
            ["bytes-in"]="0",["bytes-out"]="0",["limit-bytes-total"]="1024",["service"]="pppoe",["uptime"]="5m",["start-time"]="2026-11-15 10:00:00",
            ["end-time"]="2026-11-15 10:05:00",["session-time"]="5m",["active"]="no",
            ["download"]="1024",["upload"]="512",["address"]="10.0.0.2",
            ["session-status"]="stopped",["unused-detail"]=new string('x',1024)}).ToArray();
    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
        IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
    {
        if(command=="/system/resource/print")return Reply([new Dictionary<string,string>{{"version","6.49.19"}}],attributes);
        if(command is "/tool/user-manager/session/print" or "/tool/user-manager/user/print" or
            "/ip/hotspot/user/print" or "/ppp/secret/print" or "/ppp/active/print")return Reply(rows,attributes);
        if(command is "/tool/user-manager/profile/print" or "/ppp/profile/print")
            return Reply([new Dictionary<string,string>{[".id"]="*E",["name"]="gold",["owner"]="alice",["rate-limit"]="4M/4M"}],attributes);
        if(command is "/tool/user-manager/profile/limitation/print" or "/tool/user-manager/profile/profile-limitation/print")
            return Reply([],attributes);
        throw new InvalidOperationException(command);
    }
    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QuerySessionsForAccountAsync(
        string command,string username,string? internalId=null,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default)=>Reply(rows.Where(r=>r["username"]==username||
            r["username"]==internalId),attributes);
    private Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> Reply(
        IEnumerable<IReadOnlyDictionary<string,string>> source,IReadOnlyDictionary<string,string>? attributes)
    {
        Requests++;var fields=attributes?.GetValueOrDefault(".proplist")?.Split(',').ToHashSet();
        var result=source.Select(r=>(IReadOnlyDictionary<string,string>)r.Where(p=>fields is null||fields.Contains(p.Key)).ToDictionary()).ToArray();
        static int Word(string s){var n=Encoding.UTF8.GetByteCount(s);return n+RouterOsProtocol.EncodeLength((uint)n).Length;}
        Bytes+=Word("!done")+1;
        foreach(var row in result)Bytes+=Word("!re")+1+row.Sum(p=>Word("="+p.Key+"="+p.Value));
        return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(result);
    }
}
