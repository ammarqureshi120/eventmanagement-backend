using EventHub.Contracts.Users;

namespace EventHub.Contracts.Me;

/// <summary>
/// <c>GET /api/me</c> (FR5, AD-9): the signed-in user's identity, role, Organization (null for a System
/// Administrator), <c>version</c> (AD-24) and the nav areas the role may use.
/// </summary>
public sealed record MeResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    UserRole Role,
    MeOrganization? Organization,
    int Version,
    IReadOnlyList<NavArea> NavAreas);

/// <summary>The caller's Organization; always null for a System Administrator. Fields are added with Organizations (Epic 2).</summary>
public sealed record MeOrganization(Guid Id, string Name);
