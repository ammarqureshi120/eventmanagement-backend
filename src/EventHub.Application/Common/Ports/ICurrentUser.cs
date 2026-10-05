namespace EventHub.Application.Common.Ports;

/// <summary>The kind of caller the <c>PermissionMatrix</c> evaluates (AD-8).</summary>
public enum ActorKind
{
    Anonymous,
    SystemAdministrator,
    OrgAdministrator,
    EventManager,

    /// <summary>The scheduler and seeds dispatching through the pipeline (AD-5, AD-31).</summary>
    System,
}

/// <summary>
/// The caller of the current use case. Outside Users and Identity, features read the caller's org and role
/// only through this port or <see cref="ITenantContext"/> (AD-26). Anonymous until sign-in lands (Story 1.4).
/// </summary>
public interface ICurrentUser
{
    ActorKind Kind { get; }

    Guid? UserId { get; }

    /// <summary>Shown as the audit actor name; never logged.</summary>
    string DisplayName { get; }

    /// <summary>Null for System Administrators, the System actor and anonymous callers.</summary>
    Guid? OrganizationId { get; }
}
