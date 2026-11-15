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
    private readonly RouterReadCache<UmDataSnapshot<UmPlanRow>> _planReads=new(TimeSpan.FromSeconds(45));
    public void InvalidateUmPlansCache()=>_planReads.Invalidate();
    public async Task<UmDataSnapshot<UmPlanRow>> ReadCachedUmPlansAsync(bool force=false,CancellationToken ct=default)
    {
        var read=await _planReads.ReadAsync(ReadUmPlansAsync,x=>x.IsComplete,force,ct).ConfigureAwait(false);
        return read.Value with {ReadAt=read.ReadAt,IsFromCache=read.FromCache,
            IsComplete=read.Value.IsComplete&&!read.Invalidated,
            Diagnostic=read.Invalidated?"تغيرت الباقات أثناء القراءة؛ حدّث القائمة.":read.Value.Diagnostic};
    }
}
