using NajmCard.Core.Services;

namespace NajmCard.TestFixtures;

// In-process only. This fixture has no endpoint option, sockets or production credentials.
public sealed class VoucherSimulationClock:IVoucherExecutionClock
{
    private long _ticks;
    public TimeSpan Elapsed=>TimeSpan.FromTicks(Interlocked.Read(ref _ticks));
    public void Advance(TimeSpan time)=>Interlocked.Add(ref _ticks,time.Ticks);
    public Task DelayAsync(TimeSpan delay,CancellationToken ct){ct.ThrowIfCancellationRequested();Advance(delay);return Task.CompletedTask;}
}

public sealed class VoucherPipelineRouter:IRouterCommandClient,IRouterReceiptClient,IRouterQueryClient,IRouterNameQueryClient
{
    public readonly VoucherSimulationClock Clock=new();
    public bool Pace=true;
    public string Serial="stage4-simulation-device";
    public int Requests,Adds,Activations,Verifications,Errors;
    public int LoseActivationAt, LoseAddAt;
    public bool WrongProfile;
    public string[] ProfileOwners=["admin"];
    public Action? BeforeWrite;
    public readonly Dictionary<string,int> RequestsByPath=new();
    private readonly Dictionary<string,Dictionary<string,Dictionary<string,string>>> _byId=new();
    private readonly Dictionary<string,Dictionary<string,Dictionary<string,string>>> _byName=new();
    private readonly object _gate=new();
    private static Dictionary<string,string> Row(params string[] pairs)=>Enumerable.Range(0,pairs.Length/2).ToDictionary(i=>pairs[2*i],i=>pairs[2*i+1]);
    private async Task Tick(string path,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(Pace)await VoucherPipelineOperation.BeforeCommandAsync(ct);
        else VoucherPipelineOperation.Current?.Metrics.Request();
        Clock.Advance(TimeSpan.FromMilliseconds(50));
        lock(_gate){Requests++;RequestsByPath[path]=RequestsByPath.GetValueOrDefault(path)+1;}
    }
    private IReadOnlyList<IReadOnlyDictionary<string,string>> Full(string path)
    {
        if(_byId.TryGetValue(path,out var rows))return rows.Values.Select(x=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>(x)).ToArray();
        return path switch
        {
            "/system/routerboard/print"=>[Row("serial-number",Serial)],
            "/tool/user-manager/profile/print"=>ProfileOwners.Select(owner=>(IReadOnlyDictionary<string,string>)Row(".id","*F","name","gold","owner",owner)).ToArray(),
            "/ip/hotspot/print"=>[Row("name","hs","profile","hsprof","disabled","no")],
            "/ip/hotspot/profile/print"=>[Row("name","hsprof","use-radius","yes","radius-accounting","yes")],
            "/radius/print"=>[Row("service","hotspot","address","127.0.0.1","disabled","no")],
            "/tool/user-manager/router/print"=>[Row("name","local","ip-address","127.0.0.1","disabled","no")],_=>[]
        };
    }
    private RouterCommandReply Mutate(string path,IReadOnlyDictionary<string,string> attrs)
    {
        BeforeWrite?.Invoke();
        if(path.EndsWith("/add"))
        {
            Adds++;var menu=path[..^3]+"print";var id="*"+Adds.ToString("X");
            var row=new Dictionary<string,string>(attrs){[".id"]=id,["disabled"]="false",["incomplete"]="false"};
            if(menu.Contains("user-manager"))row["actual-profile"]="";
            if(!_byId.ContainsKey(menu)){_byId[menu]=new();_byName[menu]=new();}
            string name=row.GetValueOrDefault("username")??row["name"];
            if(_byName[menu].ContainsKey(name))throw new RouterOsTrapException("duplicate name");
            _byId[menu][id]=row;_byName[menu][name]=row;
            if(Adds==LoseAddAt){Errors++;throw new IOException("simulated lost add receipt");}
            return new([],Row("ret",id));
        }
        if(path.EndsWith("/create-and-activate-profile"))
        {
            Activations++;_byId["/tool/user-manager/user/print"][attrs["numbers"]]["actual-profile"]=attrs["profile"];
            if(Activations==LoseActivationAt){Errors++;throw new IOException("simulated lost activation receipt");}
        }
        return new([],Row());
    }
    public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string path,
        IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
    {
        await Tick(path,cancellationToken);
        lock(_gate)return path.EndsWith("/print")?Full(path):Mutate(path,attributes!).Records;
    }
    public async Task<RouterCommandReply> CommandWithReceiptAsync(string path,
        IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
    {await Tick(path,cancellationToken);lock(_gate)return Mutate(path,attributes!);}
    public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(string path,string id,
        IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
    {
        await Tick(path,cancellationToken);lock(_gate)
        {
            Verifications++;
            if(!_byId.TryGetValue(path,out var records)||!records.TryGetValue(id,out var row))return [];
            var result=new Dictionary<string,string>(row);
            if(WrongProfile)result[path.Contains("user-manager")?"actual-profile":"profile"]="wrong";
            return [result];
        }
    }
    public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByNameAsync(string path,string name,
        IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
    {
        await Tick(path,cancellationToken);lock(_gate)
            return _byName.TryGetValue(path,out var records)&&records.TryGetValue(name,out var row)?[new Dictionary<string,string>(row)]:[];
    }
}
