namespace NajmCard.Core.Services;

/// <summary>
/// An exact-name RouterOS API equality query. It must be executed server-side:
/// never enumerate an entire User Manager user table just to test a collision.
/// </summary>
public interface IRouterNameQueryClient
{
    Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByNameAsync(
        string command,string name,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default);
}
