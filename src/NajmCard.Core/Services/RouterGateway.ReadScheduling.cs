namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    // One heavy logical inventory at a time per authenticated gateway. Light
    // count-only/resource reads and exact write verification keep the fast lane.
    private readonly SemaphoreSlim _heavyReadGate=new(1,1);
    private Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> ReadInventoryCommandAsync(
        string command,IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        =>client is ResilientRouterSession session
            ?session.ReadInventoryAsync(command,attributes,cancellationToken)
            :client.CommandAsync(command,attributes,cancellationToken);
    private sealed class ReadLease(SemaphoreSlim gate):IDisposable
    {public void Dispose()=>gate.Release();}
    private async Task<IDisposable> EnterHeavyReadAsync(CancellationToken ct)
    {
        await _heavyReadGate.WaitAsync(ct).ConfigureAwait(false);
        return new ReadLease(_heavyReadGate);
    }
}
