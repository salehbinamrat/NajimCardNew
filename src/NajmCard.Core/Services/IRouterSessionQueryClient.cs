namespace NajmCard.Core.Services;

/// <summary>One server-side OR query across documented account-reference aliases.
/// Values are separate length-framed words, never RouterOS script/query text.</summary>
public interface IRouterSessionQueryClient
{
    Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QuerySessionsForAccountAsync(
        string command,string username,string? internalId=null,
        IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default);
}
