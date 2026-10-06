using EventHub.Application.Authorization;
using EventHub.Application.Common.Tenancy;
using Mediator;

namespace EventHub.Application.Auth.Logout;

/// <summary>
/// Sign out (FR1, AD-16). Runs in the Identity scope (AD-7): writes one <c>user.signedOut</c> Audit Entry and stages
/// the cookie deletion. The endpoint rotates the security stamp only after this command has committed, which ends
/// every session of the user on every device.
/// </summary>
public sealed record LogoutCommand : ICommand<SignedOutUser>, IRequirePermission, IIdentityScoped
{
    public static Permission Permission => AppPermissions.AuthLogout;
}

/// <summary>Who signed out; the endpoint rotates this user's security stamp after the commit.</summary>
public sealed record SignedOutUser(Guid UserId);
