using EventHub.Application.Authorization;
using EventHub.Contracts.Me;
using Mediator;

namespace EventHub.Application.Me.GetMe;

/// <summary>
/// <c>GET /api/me</c> (FR5, AD-9): the caller's identity, role, Organization, version and nav areas. System
/// Administrators read it in the Platform scope.
/// </summary>
public sealed record GetMeQuery : IQuery<MeResponse>, IRequirePermission
{
    public static Permission Permission => AppPermissions.MeGet;
}
