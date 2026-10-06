using EventHub.Application.Common.Ports;

namespace EventHub.Application.Common.Tenancy;

/// <summary>
/// Derives the request's data scope from the caller (AD-7): System Administrator → Platform, Organization
/// user → Tenant(orgId), System → System, anonymous (or an Organization user without an org, or an empty org id) → None.
/// An <see cref="IIdentityScoped"/> message (login, logout) switches the request to Identity instead.
/// </summary>
public sealed class CurrentUserTenantContext(ICurrentUser currentUser) : ITenantContext
{
    private bool _identity;
    private bool _read;

    public ScopeKind Scope
    {
        get
        {
            _read = true;
            return _identity
                ? ScopeKind.Identity
                : currentUser.Kind switch
                {
                    ActorKind.SystemAdministrator => ScopeKind.Platform,
                    ActorKind.OrgAdministrator or ActorKind.EventManager
                        when currentUser.OrganizationId is { } organizationId && organizationId != Guid.Empty => ScopeKind.Tenant,
                    ActorKind.System => ScopeKind.System,
                    _ => ScopeKind.None,
                };
        }
    }

    public Guid? OrganizationId => Scope == ScopeKind.Tenant ? currentUser.OrganizationId : null;

    /// <summary>
    /// Switches to Identity. Throws once the derived scope has been read (for example by a connection that already
    /// applied it as the read-only session context): switching then would leave the two out of step.
    /// </summary>
    public void UseIdentityScope()
    {
        if (_identity)
        {
            return;
        }

        if (_read)
        {
            throw new InvalidOperationException("The data scope was already read; the Identity scope must be chosen before any use (AD-7).");
        }

        _identity = true;
    }
}
