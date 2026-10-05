using EventHub.Application.Common.Ports;

namespace EventHub.Infrastructure.Persistence.Scopes;

/// <summary>
/// The data scope set on every SQL connection of a DbContext (AD-7). <see cref="SessionValue"/> is the
/// lowercase value the RLS functions compare <c>SESSION_CONTEXT(N'Scope')</c> with.
/// </summary>
public sealed record DataScope
{
    private DataScope(ScopeKind kind, Guid? organizationId)
    {
        Kind = kind;
        OrganizationId = organizationId;
    }

    public static DataScope None { get; } = new(ScopeKind.None, null);

    public static DataScope Identity { get; } = new(ScopeKind.Identity, null);

    public static DataScope Platform { get; } = new(ScopeKind.Platform, null);

    public static DataScope System { get; } = new(ScopeKind.System, null);

    public ScopeKind Kind { get; }

    /// <summary>Set only for <see cref="ScopeKind.Tenant"/>.</summary>
    public Guid? OrganizationId { get; }

    /// <summary><c>tenant</c> | <c>platform</c> | <c>identity</c> | <c>system</c>; null for <see cref="None"/>.</summary>
    public string? SessionValue => Kind == ScopeKind.None ? null : Kind.ToString().ToLowerInvariant();

    public static DataScope Tenant(Guid organizationId) =>
        organizationId == Guid.Empty
            ? throw new ArgumentException("A tenant scope needs an Organization id.", nameof(organizationId))
            : new DataScope(ScopeKind.Tenant, organizationId);

    public static DataScope From(ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        return tenantContext.Scope switch
        {
            ScopeKind.Tenant when tenantContext.OrganizationId is { } organizationId => Tenant(organizationId),
            ScopeKind.Platform => Platform,
            ScopeKind.Identity => Identity,
            ScopeKind.System => System,
            _ => None,
        };
    }
}
