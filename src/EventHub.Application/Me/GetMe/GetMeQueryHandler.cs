using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using EventHub.Contracts.Me;
using Mediator;
using DomainRole = EventHub.Domain.Users.UserRole;
using WireRole = EventHub.Contracts.Users.UserRole;

namespace EventHub.Application.Me.GetMe;

/// <summary>Projects the caller's profile and the nav areas of their role (AD-9, AD-20).</summary>
public sealed class GetMeQueryHandler(ICurrentUser currentUser, IUserProfileReader profiles)
    : IQueryHandler<GetMeQuery, MeResponse>
{
    public async ValueTask<MeResponse> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotFoundException("No signed-in user.");
        var profile = await profiles.FindAsync(userId, cancellationToken)
                      ?? throw new NotFoundException("The signed-in user was not found.");

        return new MeResponse(
            profile.Id,
            profile.Email,
            profile.FirstName,
            profile.LastName,
            profile.Phone,
            ToWire(profile.Role),
            // Organizations arrive in Epic 2; a System Administrator never has one.
            Organization: null,
            profile.Version,
            NavAreasFor(profile.Role));
    }

    /// <summary>
    /// The areas each role may use. In Epic 1 only the System Administrator signs in: the platform home and
    /// My account (the UI keeps "My account" hidden until Story 1.7 builds it).
    /// </summary>
    public static IReadOnlyList<NavArea> NavAreasFor(DomainRole role) => role switch
    {
        DomainRole.SystemAdministrator => [NavArea.Platform, NavArea.Account],
        _ => [NavArea.Account],
    };

    private static WireRole ToWire(DomainRole role) => role switch
    {
        DomainRole.SystemAdministrator => WireRole.SystemAdministrator,
        DomainRole.OrgAdministrator => WireRole.OrgAdministrator,
        DomainRole.EventManager => WireRole.EventManager,
        _ => throw new InvalidOperationException($"Unknown user role {role}."),
    };
}
