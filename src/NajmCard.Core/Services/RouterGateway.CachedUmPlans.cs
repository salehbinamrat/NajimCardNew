using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// A short-lived, per-router cache for profile/limitation definitions only.
/// Keeps a slow v6 router from repeating three inventory reads on navigation.
/// Explicit refresh and confirmed/uncertain writes invalidate the snapshot.
/// Passwords, sessions and voucher user lists are never cached here.
/// </summary>
public sealed partial class RouterGateway
{
    private readonly SemaphoreSlim _umPlansReadGate=new(1,1);
    private readonly object _umPlansCacheLock=new();
    private UmDataSnapshot<UmPlanRow>? _umPlansCache;
    private DateTimeOffset _umPlansCachedAt;
    private static readonly TimeSpan UmPlansTtl=TimeSpan.FromSeconds(45);

    public void InvalidateUmPlansCache()
    {
        lock(_umPlansCacheLock)
        {
            _umPlansCache=null;
            _umPlansCachedAt=DateTimeOffset.MinValue;
        }
    }

    private UmDataSnapshot<UmPlanRow>? CurrentPlansCache()
    {
        lock(_umPlansCacheLock)
            return _umPlansCache is {IsComplete:true} cached &&
                DateTimeOffset.UtcNow-_umPlansCachedAt<UmPlansTtl
                ?cached:null;
    }

    public async Task<UmDataSnapshot<UmPlanRow>> ReadCachedUmPlansAsync(
        bool force=false,CancellationToken ct=default)
    {
        if(!force && CurrentPlansCache() is { } hit)return hit;
        await _umPlansReadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if(!force && CurrentPlansCache() is { } hit)return hit;
            var snapshot=await ReadUmPlansAsync(ct).ConfigureAwait(false);
            lock(_umPlansCacheLock)
            {
                _umPlansCache=snapshot.IsComplete?snapshot:null;
                _umPlansCachedAt=DateTimeOffset.UtcNow;
            }
            return snapshot;
        }
        finally{_umPlansReadGate.Release();}
    }
}
