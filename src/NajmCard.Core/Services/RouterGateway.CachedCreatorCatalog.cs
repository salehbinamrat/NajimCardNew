using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Per-authenticated-router bounded cache for the SMALL, read-only creator
/// catalog. Never cache or expose passwords or a user inventory. This also
/// survives recreation of GeneratorView when switching between pages.
/// </summary>
public sealed partial class RouterGateway
{
    private readonly RouterReadCache<IReadOnlyList<UserManagerV6Profile>> _creatorProfiles=new(TimeSpan.FromMinutes(2));
    private readonly RouterReadCache<IReadOnlyList<UserManagerV6Customer>> _creatorCustomers=new(TimeSpan.FromMinutes(2));
    public long CreatorCatalogRevision=>_creatorProfiles.Revision;
    public DateTimeOffset? CreatorCatalogReadAt=>_creatorProfiles.ReadAt;
    public async Task<IReadOnlyList<UserManagerV6Profile>> ReadCachedCreatorProfilesAsync(
        bool force=false,CancellationToken ct=default)
    {
        var read=await _creatorProfiles.ReadAsync(ReadUserManagerV6ProfilesAsync,_=>true,force,ct);
        if(read.Invalidated)throw new InvalidOperationException("تغيرت الباقات أثناء القراءة؛ حدّث القائمة.");
        return read.Value;
    }
    public async Task<IReadOnlyList<UserManagerV6Customer>> ReadCachedCreatorCustomersAsync(
        bool force=false,CancellationToken ct=default)
    {
        var read=await _creatorCustomers.ReadAsync(ReadUserManagerV6CustomersAsync,_=>true,force,ct);
        if(read.Invalidated)throw new InvalidOperationException("تغيرت بيانات المالك أثناء القراءة؛ حدّث القائمة.");
        return read.Value;
    }
    public void InvalidateCreatorCatalog(){_creatorProfiles.Invalidate();_creatorCustomers.Invalidate();}
}
