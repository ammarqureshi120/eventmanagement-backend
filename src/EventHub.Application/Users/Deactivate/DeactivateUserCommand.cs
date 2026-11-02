using EventHub.Application.Authorization;
using Mediator;

namespace EventHub.Application.Users.Deactivate;

/// <summary>
/// Deactivate a User (FR2, AD-26). Story 1.5 ships no endpoint, route or UI for it (Epic 2 builds those): it is
/// dispatched only by the test Arrange hook, as the System actor in the System scope. The caller rotates the
/// user's security stamp after this command commits, so every session of the user ends on its next request.
/// </summary>
public sealed record DeactivateUserCommand(Guid UserId) : ICommand, IRequirePermission
{
    public static Permission Permission => AppPermissions.UsersDeactivate;
}
