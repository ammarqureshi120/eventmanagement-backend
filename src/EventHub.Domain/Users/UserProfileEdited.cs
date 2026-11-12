using EventHub.Domain.Common;

namespace EventHub.Domain.Users;

/// <summary>Raised when a User's own profile (names, phone) changed (FR36). Carries the role and Organization the audit visibility needs.</summary>
public sealed record UserProfileEdited(Guid UserId, UserRole Role, Guid? OrganizationId) : IDomainEvent;
