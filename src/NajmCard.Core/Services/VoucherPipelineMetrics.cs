using System.Diagnostics;

namespace NajmCard.Core.Services;

public enum VoucherPipelineStage { Generation, NameCheck, SQLite, Add, Activation, Verification, Save, Print, Identity, Readiness }
public sealed record VoucherPipelineProgress(int Total,int Confirmed,int Failed,int Unknown);
public sealed record VoucherPipelineSnapshot(IReadOnlyDictionary<string,double> StageMilliseconds,
    long RouterRequests,long ReadRetries,long VerificationRetries,double ThrottleMilliseconds,
    int Total,int Confirmed,int Failed,int Unknown);

/// <summary>Monotonic, credential-free measurements. Outcomes advance only after durable commit.</summary>
public sealed class VoucherPipelineMetrics
{
    private readonly object _gate=new();
    private readonly Dictionary<string,double> _stages=Enum.GetNames<VoucherPipelineStage>().ToDictionary(x=>x,_=>0d);
    private long _requests,_readRetries,_pollRetries;
    private double _throttle;
    private int _total,_confirmed,_failed,_unknown;
    public IProgress<VoucherPipelineProgress>? Progress {get;set;}
    public IDisposable Measure(VoucherPipelineStage stage)=>new Measurement(this,stage.ToString());
    private sealed class Measurement(VoucherPipelineMetrics owner,string stage):IDisposable
    {
        private readonly long _start=Stopwatch.GetTimestamp();private int _closed;
        public void Dispose()
        {if(Interlocked.Exchange(ref _closed,1)==0)lock(owner._gate)owner._stages[stage]+=Stopwatch.GetElapsedTime(_start).TotalMilliseconds;}
    }
    public void Request(){lock(_gate)_requests++;}
    public void ReadRetry(){lock(_gate)_readRetries++;}
    public void VerificationRetry(){lock(_gate)_pollRetries++;}
    internal void Throttled(TimeSpan time){lock(_gate)_throttle+=time.TotalMilliseconds;}
    public void SetTotal(int total){lock(_gate)_total=total;Publish();}
    public void Committed(string state)
    {
        int total,processed;
        lock(_gate)
        {
            if(state=="SENT")_confirmed++;else if(state=="FAILED")_failed++;else if(state=="UNKNOWN")_unknown++;
            total=_total;processed=_confirmed+_failed+_unknown;
        }
        if(total<=24||processed==1||processed==total||processed%Math.Max(1,total/100)==0||state!="SENT")Publish();
    }
    private void Publish()
    {
        VoucherPipelineProgress progress;lock(_gate)progress=new(_total,_confirmed,_failed,_unknown);
        Progress?.Report(progress);
    }
    public VoucherPipelineSnapshot Snapshot()
    {lock(_gate)return new(new Dictionary<string,double>(_stages),_requests,_readRetries,_pollRetries,_throttle,_total,_confirmed,_failed,_unknown);}
}

public interface IVoucherExecutionClock
{
    TimeSpan Elapsed {get;}
    Task DelayAsync(TimeSpan delay,CancellationToken cancellationToken);
}
public sealed class SystemVoucherExecutionClock : IVoucherExecutionClock
{
    private readonly long _start=Stopwatch.GetTimestamp();
    public TimeSpan Elapsed=>Stopwatch.GetElapsedTime(_start);
    public Task DelayAsync(TimeSpan delay,CancellationToken cancellationToken)=>Task.Delay(delay,cancellationToken);
}

/// <summary>One command at most every 50 ms by default, without catch-up bursts after slow replies.</summary>
public sealed class VoucherCommandPacer
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly IVoucherExecutionClock _clock;
    private readonly TimeSpan _interval;
    private TimeSpan? _last;
    public VoucherCommandPacer(IVoucherExecutionClock? clock=null,TimeSpan? interval=null)
    {
        _clock=clock??new SystemVoucherExecutionClock();_interval=interval??TimeSpan.FromMilliseconds(50);
        if(_interval<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(interval));
    }
    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var start=_clock.Elapsed;
            if(_last is { } last)
                while(_clock.Elapsed-last<_interval)
                    await _clock.DelayAsync(_interval-(_clock.Elapsed-last),ct);
            ct.ThrowIfCancellationRequested();
            _last=_clock.Elapsed;
            VoucherPipelineOperation.Current?.Metrics.Throttled(_clock.Elapsed-start);
        }
        finally{_gate.Release();}
    }
}

/// <summary>Flows through asynchronous work, including the existing two RouterOS lanes.</summary>
public sealed class VoucherPipelineOperation : IDisposable
{
    private static readonly AsyncLocal<VoucherPipelineOperation?> Ambient=new();
    private static readonly VoucherCommandPacer DefaultPacer=new();
    private readonly VoucherPipelineOperation? _previous;
    public static VoucherPipelineOperation? Current=>Ambient.Value;
    public VoucherPipelineMetrics Metrics {get;}
    private readonly VoucherCommandPacer _pacer;
    public VoucherPipelineOperation(VoucherPipelineMetrics? metrics=null,VoucherCommandPacer? pacer=null)
    {
        _previous=Ambient.Value;Metrics=metrics??_previous?.Metrics??new();
        _pacer=pacer??_previous?._pacer??DefaultPacer;Ambient.Value=this;
    }
    public static IDisposable? Measure(VoucherPipelineStage stage)=>Current?.Metrics.Measure(stage);
    public static async Task BeforeCommandAsync(CancellationToken ct)
    {
        if(Current is not { } operation)return;
        await operation._pacer.WaitAsync(ct);operation.Metrics.Request();
    }
    public void Dispose()=>Ambient.Value=_previous;
}
