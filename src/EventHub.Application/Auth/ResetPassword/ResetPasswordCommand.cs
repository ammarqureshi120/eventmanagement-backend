using EventHub.Application.Authorization;
using EventHub.Application.Common.Tenancy;
using Mediator;

namespace EventHub.Application.Auth.ResetPassword;

/// <summary>
/// Set a new password with a reset token (FR4, FR37, AD-16). Anonymous, in the Identity scope (AD-7). The token is
/// checked first (<c>token_invalid</c>), then the password policy (<c>validation</c> on <c>newPassword</c>, AD-17).
/// Success rotates the security stamp, clears lockout and writes one <c>user.passwordResetCompleted</c> Audit Entry.
/// </summary>
public sealed record ResetPasswordCommand(string? Token, string? NewPassword) : ICommand, IRequirePermission, IIdentityScoped
{
    public static Permission Permission => AppPermissions.AuthResetPassword;

    /// <summary>Never prints the token or the password (NFR4, AD-22).</summary>
    public override string ToString() => nameof(ResetPasswordCommand);
}
