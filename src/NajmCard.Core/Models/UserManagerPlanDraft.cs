namespace NajmCard.Core.Models;

/// <summary>Inputs explicitly confirmed by the operator for RouterOS 7 profile edits.</summary>
public sealed record UmPlanDraft(
    string Kind,string Name,string Price,string Validity,string StartsWhen,
    string TransferLimit,string DownloadLimit,string UploadLimit,string UptimeLimit,
    string RateRx,string RateTx="")
{
    // Required explicitly for legacy v6 writes; v7 does not use it.
    public string Owner { get; init; }="";
}

public sealed record UmPlanMutationResult(bool Success,bool Uncertain,string Message);
