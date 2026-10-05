namespace EventHub.Domain.Users;

/// <summary>Stored as varchar (AD-19).</summary>
public enum UserStatus
{
    Invited,
    Active,
    Inactive,
}
