using NajmCard.Core.Models;

namespace NajmCard.Core.Models;

/// <summary>
/// Snapshot captured when the operator selected exactly one User Manager user.
/// No password is retained in this value.
/// </summary>
public sealed record UserManagerAccountSelection(
    RouterOsGeneration Generation, string Id, string Name, bool Disabled);

/// <summary>
/// Uncertain=True means a write may have reached RouterOS. Never retry
/// automatically or report the action as successful without readback proof.
/// </summary>
public sealed record UserManagerMutationResult(
    bool Success, bool Uncertain, string Message);
