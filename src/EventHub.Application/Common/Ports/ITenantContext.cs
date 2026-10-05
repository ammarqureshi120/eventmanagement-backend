namespace EventHub.Application.Common.Ports;

/// <summary>The data scope a use case runs in (AD-7). <see cref="None"/> fails closed: RLS returns no rows.</summary>
public enum ScopeKind
{
    None,
    Tenant,
    Platform,
    Identity,
    System,
}

/// <summary>
/// The request's data scope (AD-7): <c>Tenant(orgId)</c> for Organization users, <c>Platform</c> for System
/// Administrators, <c>System</c> for the System actor, <c>None</c> when anonymous. The Infrastructure scope
/// accessor sets the SQL session context from it on every connection open.
/// </summary>
public interface ITenantContext
{
    ScopeKind Scope { get; }

    /// <summary>Set only when <see cref="Scope"/> is <see cref="ScopeKind.Tenant"/>.</summary>
    Guid? OrganizationId { get; }
}
