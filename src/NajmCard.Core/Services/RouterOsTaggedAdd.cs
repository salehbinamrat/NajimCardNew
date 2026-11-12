namespace NajmCard.Core.Services;

/// <summary>
/// Bounded RouterOS API pipelining for independent Hotspot/PPPoE /add
/// operations. All caller-owned vouchers MUST already hold durable SENDING
/// leases before this method is invoked. Never use for UM profile activation.
/// </summary>
public sealed record RouterTaggedAddCommand(
    string Path, IReadOnlyDictionary<string,string> Attributes);

/// <param name="Rejected">RouterOS explicitly returned !trap for this tag,
/// followed by !done. No missing/ambiguous reply is a rejection.</param>
public sealed record RouterTaggedAddOutcome(
    bool Rejected, string? ReturnId, string? Error);

public interface IRouterTaggedAddClient
{
    Task<IReadOnlyList<RouterTaggedAddOutcome>> AddWindowAsync(
        IReadOnlyList<RouterTaggedAddCommand> commands,
        CancellationToken cancellationToken=default);
}
