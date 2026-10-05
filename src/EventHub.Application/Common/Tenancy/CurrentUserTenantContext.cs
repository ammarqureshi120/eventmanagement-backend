using EventHub.Application.Common.Ports;

namespace EventHub.Application.Common.Tenancy;

/// <summary>
/// Derives the request's data scope from the caller (AD-7): System Administrator → Platform, Organization
/// user → Tenant(orgId), System → System, anonymous (or an Organization user without an org, or an empty org id) → None.
/// </summary>
public sealed class CurrentUserTenantContext(ICurrentUser currentUser) : ITenantContext
{
    public ScopeKind Scope => currentUser.Kind switch
    {
        ActorKind.SystemAdministrator => ScopeKind.Platform,
        ActorKind.OrgAdministrator or ActorKind.EventManager
            when currentUser.OrganizationId is { } organizationId && organizationId != Guid.Empty => ScopeKind.Tenant,
        ActorKind.System => ScopeKind.System,
        _ => ScopeKind.None,
    };

    public Guid? OrganizationId => Scope == ScopeKind.Tenant ? currentUser.OrganizationId : null;
}
