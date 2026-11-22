namespace NajmCard.Core.Services;

public sealed record VoucherReadbackEvidence(bool Confirmed,bool MayBecomeReady,string Error="");

/// <summary>Reads only: immediate verification, then bounded polling of a pending result.</summary>
public static class VoucherReadbackPoller
{
    public const int MaximumReads=5;
    public static async Task<VoucherReadbackEvidence> VerifyAsync(
        Func<CancellationToken,Task<VoucherReadbackEvidence>> read,CancellationToken ct=default,
        IVoucherExecutionClock? clock=null)
    {
        clock??=new SystemVoucherExecutionClock();
        var started=clock.Elapsed;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Includes throttling and slow reads, so polling cannot extend a
        // command's latency indefinitely. Cancellation leaves delivery UNKNOWN.
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var token=deadline.Token;
        var last=new VoucherReadbackEvidence(false,true,"لم يظهر الحساب وباقته بعد.");
        int[] atMilliseconds=[0,50,150,350,750];
        for(int attempt=0;attempt<MaximumReads;attempt++)
        {
            var delay=TimeSpan.FromMilliseconds(atMilliseconds[attempt])-(clock.Elapsed-started);
            if(delay>TimeSpan.Zero)await clock.DelayAsync(delay,token);
            token.ThrowIfCancellationRequested();
            if(clock.Elapsed-started>=TimeSpan.FromSeconds(20))break;
            if(attempt>0)VoucherPipelineOperation.Current?.Metrics.VerificationRetry();
            last=await read(token);
            if(last.Confirmed||!last.MayBecomeReady)return last;
        }
        return last;
    }
}
