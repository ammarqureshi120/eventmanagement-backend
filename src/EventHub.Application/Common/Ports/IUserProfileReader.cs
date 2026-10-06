using EventHub.Domain.Users;

namespace EventHub.Application.Common.Ports;

/// <summary>
/// Read port for a user's own profile (AD-20): a no-tracking projection in the request's data scope, so RLS applies
/// (a System Administrator reads in the Platform scope). Never exposes credentials.
/// </summary>
public interface IUserProfileReader
{
    Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>The profile fields <c>GET /api/me</c> returns.</summary>
public sealed record UserProfile(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    UserRole Role,
    Guid? OrganizationId,
    int Version);
