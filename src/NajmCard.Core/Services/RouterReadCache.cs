namespace NajmCard.Core.Services;

internal sealed record RouterReadResult<T>(T Value,DateTimeOffset ReadAt,bool FromCache,bool Invalidated,long Revision);

/// <summary>Per-gateway cache. Serializes equivalent reads, including concurrent
/// forced refreshes. An invalidation before/during a read cannot publish stale data.</summary>
internal sealed class RouterReadCache<T>(TimeSpan ttl)
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly object sync=new();
    private RouterReadResult<T>? saved;
    private long revision,completed;
    private int writers;
    public long Revision { get {lock(sync)return revision;} }
    public DateTimeOffset? ReadAt {get{lock(sync)return saved?.ReadAt;}}
    public bool IsStable(long epoch){lock(sync)return revision==epoch&&writers==0;}
    public void Invalidate(){lock(sync){revision++;saved=null;}}
    public void BeginWrite(){lock(sync){writers++;revision++;saved=null;}}
    public void EndWrite(){lock(sync){writers--;revision++;saved=null;}}
    public async Task<RouterReadResult<T>> ReadAsync(Func<CancellationToken,Task<T>> read,
        Func<T,bool> valid,bool force,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        long seen;
        lock(sync)
        {
            seen=completed;
            if(!force&&saved is {} hit&&DateTimeOffset.UtcNow-hit.ReadAt<ttl)
                return hit with{FromCache=true};
        }
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long epoch;
            lock(sync)
            {
                if((!force||completed>seen)&&saved is {} hit&&DateTimeOffset.UtcNow-hit.ReadAt<ttl)
                    return hit with{FromCache=true};
                epoch=revision;
            }
            var at=DateTimeOffset.UtcNow;
            T value;
            try{value=await read(ct).ConfigureAwait(false);}
            catch{lock(sync)saved=null;throw;}
            lock(sync)
            {
                var result=new RouterReadResult<T>(value,at,false,epoch!=revision||writers>0,epoch);
                completed++;
                saved=!result.Invalidated&&valid(value)?result:null;
                return result;
            }
        }
        finally{gate.Release();}
    }
}
