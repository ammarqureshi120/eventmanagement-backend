namespace EventHub.Domain.Users;

/// <summary>Stored as varchar (AD-19). <see cref="SystemAdministrator"/> is the only role without an Organization (AD-26).</summary>
public enum UserRole
{
    SystemAdministrator,
    OrgAdministrator,
    EventManager,
}
