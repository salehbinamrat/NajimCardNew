namespace NajmCard.Core.Models;

public sealed record Voucher(string Username, string Password, string Profile,
    string BatchId, DateTimeOffset CreatedAt, bool SentToRouter = false,
    string Service = "hotspot", string Customer = "", string DeliveryStatus = "CREATED")
{
    // Empty identifies legacy/unassigned history, never the currently connected router.
    public string RouterId { get; init; }="";
    public string AttemptId { get; init; }="";
    public string RouterRecordId { get; init; }="";
    public string UpdatedUtc { get; init; }="";
    public string LastError { get; init; }="";
    public string DeliveryTag=>RouterId.Length==0?"NajmCard batch "+BatchId:
        "NC2:"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(
                new[]{RouterId,Service,BatchId,Username})))).ToLowerInvariant();
}

public sealed record VoucherReconciliation(string State,string Reason,string RouterRecordId="");

public sealed record VoucherGenerationOptions(int Count, string Prefix, int Length,
    string Characters, bool PasswordEqualsUsername, string Profile,
    int PasswordLength = 0, string? PasswordCharacters = null,
    bool LengthIncludesPrefix = false, bool UsernameOnly = false);

public sealed record BatchDeliveryResult(int Succeeded, IReadOnlyList<string> Failed,
    bool UncertainOutcome)
{
    public bool Success => Failed.Count == 0 && !UncertainOutcome;
}
