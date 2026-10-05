namespace EventHub.Domain.Common;

/// <summary>
/// Tenant-owned entities carry a non-null <c>OrganizationId</c> and get the EF "Tenant" query filter
/// (<c>HasTenantFilter()</c>) plus RLS via <c>AddTenantRls</c> (AD-7, AD-30). Venues, Events and the other
/// tenant tables join in later stories.
/// </summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; }
}
