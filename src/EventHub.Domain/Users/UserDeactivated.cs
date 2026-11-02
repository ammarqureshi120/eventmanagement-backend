using EventHub.Domain.Common;

namespace EventHub.Domain.Users;

/// <summary>Raised when a User becomes Inactive (FR2). Carries the role and Organization the audit visibility needs.</summary>
public sealed record UserDeactivated(Guid UserId, UserRole Role, Guid? OrganizationId) : IDomainEvent;
