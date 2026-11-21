using System.Diagnostics;
using System.Text.Json;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using NajmCard.TestFixtures;

// No endpoint parameters, sockets or real printers. WAL/FULL SQLite is real.
if(args.Length!=1)throw new ArgumentException("Usage: VoucherPipelineBenchmarks output.jsonl");
var destination=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
using var output=new StreamWriter(destination,false);
foreach(var size in new[]{1,100,1000,10000})
foreach(var service in new[]{"hotspot","pppoe","userman"})
{
    var samples=new List<Sample>();
    for(int repeat=-1;repeat<3;repeat++)
    {
        var sample=await Run(size,service);if(repeat>=0)samples.Add(sample);
    }
    double Median(IEnumerable<double> values)=>values.Order().ElementAt(samples.Count/2);
    var first=samples[0];
    if(samples.Any(s=>s.Requests!=first.Requests||s.Confirmed!=size||s.Errors!=0||s.Retries!=0))
        throw new InvalidOperationException("Non-repeatable or failed simulation");
    var stages=first.Stages.Keys.ToDictionary(k=>k,k=>k=="Print"?(double?)null:Median(samples.Select(s=>s.Stages[k])));
    output.WriteLine(JsonSerializer.Serialize(new{size,service,samples=samples.Count,stages_ms=stages,
        elapsed_ms=Median(samples.Select(s=>s.Elapsed)),requests=first.Requests,confirmed=first.Confirmed,
        readbacks=first.Readbacks,errors=first.Errors,retries=first.Retries,
        modeled_network_ms=first.ModeledNetwork,throttle_ms=first.Throttle,
        print_measurement="Separate Windows WPF benchmark; no physical printer"}));
    output.Flush();Console.WriteLine($"{service}/{size}: {first.Requests} requests, {first.Confirmed} confirmed");
}

static async Task<Sample> Run(int size,string service)
{
    var root=Path.Combine(Path.GetTempPath(),"najm-pipeline-benchmark",Guid.NewGuid().ToString("N"));
    try
    {
        using var store=new VoucherStore(root);
        var router=new VoucherPipelineRouter
        {
#if STAGE4
            Pace=true
#else
            Pace=false
#endif
        };
        var metrics=new VoucherPipelineMetrics();
        using var operation=new VoucherPipelineOperation(metrics,new VoucherCommandPacer(router.Clock));
        await using var session=new ResilientRouterSession(router,_=>Task.FromResult<IRouterCommandClient>(router));
        var gateway=new RouterGateway(session);
        var options=new VoucherGenerationOptions(size,"SIM-",12,VoucherGenerator.Digits,false,"gold",UsernameOnly:true);
        var timer=Stopwatch.StartNew();IReadOnlyList<Voucher> batch;
#if STAGE4
        batch=await new VoucherBatchPreparer(gateway,store).PrepareAsync(options,service,service=="userman"?"admin":"");
#else
        // Exact pre-phase-4 UI ordering: local generation/save, then dispatch preflight.
        var id=await gateway.ReadRouterIdentityAsync();
        batch=VoucherGenerator.Generate(options,store.ReservedNames(service,id))
            .Select(c=>c with{RouterId=id,Service=service,Customer=service=="userman"?"admin":""}).ToArray();
        store.SaveBatch(batch);
#endif
        var result=await new VoucherBatchDispatcher(gateway,store).SendAsync(batch);
        if(!result.Success||result.Succeeded!=size)throw new InvalidOperationException("Delivery simulation failed");
        var persisted=store.GetBatchVouchers(service,batch[0].BatchId,batch[0].RouterId);
        if(persisted.Count!=size||persisted.Any(c=>c.DeliveryStatus!="SENT"))throw new InvalidOperationException("Lost durable receipt");
#if !STAGE4
        using(VoucherPipelineOperation.Measure(VoucherPipelineStage.Save))
#endif
            VoucherBatchCsvExporter.Export(persisted,Path.Combine(root,"confirmed.csv"));
        timer.Stop();var snapshot=metrics.Snapshot();
        return new(snapshot.StageMilliseconds,timer.Elapsed.TotalMilliseconds,router.Requests,result.Succeeded,
            router.Verifications,router.Errors,(int)(snapshot.ReadRetries+snapshot.VerificationRetries),
            router.Clock.Elapsed.TotalMilliseconds,snapshot.ThrottleMilliseconds);
    }
    finally{if(Directory.Exists(root))Directory.Delete(root,true);}
}
record Sample(IReadOnlyDictionary<string,double> Stages,double Elapsed,int Requests,int Confirmed,
    int Readbacks,int Errors,int Retries,double ModeledNetwork,double Throttle);
