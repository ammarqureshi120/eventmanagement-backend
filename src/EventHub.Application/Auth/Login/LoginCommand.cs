using EventHub.Application.Authorization;
using EventHub.Application.Common.Tenancy;
using Mediator;

namespace EventHub.Application.Auth.Login;

/// <summary>
/// Sign in with email and password (FR1, AD-16). The only Anonymous cell of the matrix; runs in the Identity scope
/// (AD-7). Success stages the session cookie and writes one <c>user.signedIn</c> Audit Entry.
/// </summary>
public sealed record LoginCommand(string? Email, string? Password) : ICommand, IRequirePermission, IIdentityScoped
{
    public static Permission Permission => AppPermissions.AuthLogin;

    /// <summary>Never prints the password or the email (NFR4, AD-22).</summary>
    public override string ToString() => nameof(LoginCommand);
}
