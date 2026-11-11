namespace NajmCard.Core.Models;

/// <summary>
/// Creation may be partially applied (account saved but profile not assigned).
/// Never represent an incomplete User Manager account as a working voucher.
/// </summary>
public sealed record UserManagerV7ProvisionResult(
    bool Success, bool Uncertain, bool AccountCreated, bool ProfileAssigned,
    string Username, string? AccountId, string Message);
