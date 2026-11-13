using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Per-authenticated-router bounded cache for the SMALL, read-only creator
/// catalog. Never cache or expose passwords or a user inventory. This also
/// survives recreation of GeneratorView when switching between pages.
/// </summary>
public sealed partial class RouterGateway
{
    private readonly SemaphoreSlim _creatorProfileGate=new(1,1);
    private readonly SemaphoreSlim _creatorCustomerGate=new(1,1);
    private IReadOnlyList<UserManagerV6Profile>? _cachedCreatorProfiles;
    private IReadOnlyList<UserManagerV6Customer>? _cachedCreatorCustomers;
    private DateTimeOffset _creatorProfilesAt;
    private DateTimeOffset _creatorCustomersAt;
    private static readonly TimeSpan CreatorCatalogTtl=TimeSpan.FromMinutes(2);

    public async Task<IReadOnlyList<UserManagerV6Profile>>
        ReadCachedCreatorProfilesAsync(bool force=false,CancellationToken ct=default)
    {
        if(!force && _cachedCreatorProfiles is { } saved &&
            DateTimeOffset.UtcNow-_creatorProfilesAt<CreatorCatalogTtl)return saved;
        await _creatorProfileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if(!force && _cachedCreatorProfiles is { } current &&
                DateTimeOffset.UtcNow-_creatorProfilesAt<CreatorCatalogTtl)return current;
            // A failed refresh leaves the prior cache untouched but DOES NOT
            // claim it is fresh; the caller sees the exception.
            var profiles=await ReadUserManagerV6ProfilesAsync(ct).ConfigureAwait(false);
            _cachedCreatorProfiles=profiles;
            _creatorProfilesAt=DateTimeOffset.UtcNow;
            return profiles;
        }
        finally{_creatorProfileGate.Release();}
    }

    public async Task<IReadOnlyList<UserManagerV6Customer>>
        ReadCachedCreatorCustomersAsync(bool force=false,CancellationToken ct=default)
    {
        if(!force && _cachedCreatorCustomers is { } saved &&
            DateTimeOffset.UtcNow-_creatorCustomersAt<CreatorCatalogTtl)return saved;
        await _creatorCustomerGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if(!force && _cachedCreatorCustomers is { } current &&
                DateTimeOffset.UtcNow-_creatorCustomersAt<CreatorCatalogTtl)return current;
            var customers=await ReadUserManagerV6CustomersAsync(ct).ConfigureAwait(false);
            _cachedCreatorCustomers=customers;
            _creatorCustomersAt=DateTimeOffset.UtcNow;
            return customers;
        }
        finally{_creatorCustomerGate.Release();}
    }

    public void InvalidateCreatorCatalog()
    {
        _cachedCreatorProfiles=null;
        _cachedCreatorCustomers=null;
        _creatorProfilesAt=_creatorCustomersAt=DateTimeOffset.MinValue;
    }
}
