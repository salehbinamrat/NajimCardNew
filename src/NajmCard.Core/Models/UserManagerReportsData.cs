namespace NajmCard.Core.Models;

public sealed record UmApprovedPayment(
    string Id,decimal Amount,string Currency,DateTime? Date,string Profile);

public sealed record UmRouterBackup(
    string Id,string Name,string Size,string CreatedAt);
