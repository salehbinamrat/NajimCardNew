using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Short-lived generation snapshot for a single authenticated router gateway.
/// The generation is read from RouterOS, never guessed from User Manager paths.
/// An explicit force bypasses the cache, and failures are never cached.
/// </summary>
public sealed partial class RouterGateway
{
    private readonly SemaphoreSlim _generationReadGate=new(1,1);
    private RouterOsGeneration? _cachedRouterOsGeneration;
    private DateTimeOffset _generationReadAt;
    private static readonly TimeSpan GenerationTtl=TimeSpan.FromSeconds(45);

    public async Task<RouterOsGeneration> ReadCachedRouterOsGenerationAsync(
        bool force=false,CancellationToken ct=default)
    {
        await _generationReadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if(!force && _cachedRouterOsGeneration is { } recent &&
               DateTimeOffset.UtcNow-_generationReadAt<GenerationTtl)
                return recent;
            var generation=await ReadRouterOsGenerationAsync(ct).ConfigureAwait(false);
            _cachedRouterOsGeneration=generation;
            _generationReadAt=DateTimeOffset.UtcNow;
            return generation;
        }
        finally { _generationReadGate.Release(); }
    }

    public void InvalidateRouterOsGenerationCache()
    {
        // Never mutate the cache while a validation is in flight; this
        // synchronous path is intended for owners after reconnect/reboot.
        _generationReadGate.Wait();
        try
        {
            _cachedRouterOsGeneration=null;
            _generationReadAt=DateTimeOffset.MinValue;
        }
        finally { _generationReadGate.Release(); }
    }
}
