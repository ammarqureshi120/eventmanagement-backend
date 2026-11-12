using EventHub.Application.Authorization;
using Mediator;

namespace EventHub.Application.Me.UpdateMyProfile;

/// <summary>
/// Edit the caller's own first name, last name and phone (FR36, AD-24). The user is always the caller
/// (<c>ICurrentUser.UserId</c>); nothing in the body chooses who is edited. Runs in the System Administrator's
/// Platform scope. A stale <see cref="Version"/> is <c>concurrency_conflict</c>; a save with no change is a no-op
/// (no version bump, no audit); a change writes one <c>user.edited</c> Audit Entry.
/// </summary>
public sealed record UpdateMyProfileCommand(string? FirstName, string? LastName, string? Phone, int? Version)
    : ICommand, IRequirePermission
{
    public static Permission Permission => AppPermissions.MeEditProfile;
}
