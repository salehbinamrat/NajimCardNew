namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    // Invalidate on BOTH sides of a mutation, including traps, cancellation and
    // uncertain outcomes. Readers must not cache an intermediate account state.
    private sealed class ReadInvalidation(Action invalidate):IDisposable
    {public void Dispose()=>invalidate();}

    private IDisposable TrackUserManagerMutation(bool plans=false)
    {
        _lifeReads.BeginWrite();_sessionReads.BeginWrite();
        if(plans){_planReads.BeginWrite();_creatorProfiles.BeginWrite();_creatorCustomers.BeginWrite();}
        return new ReadInvalidation(()=>
        {
            _lifeReads.EndWrite();_sessionReads.EndWrite();
            if(plans){_planReads.EndWrite();_creatorProfiles.EndWrite();_creatorCustomers.EndWrite();}
        });
    }
}
