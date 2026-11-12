using EventHub.Application.Authorization;
using Mediator;

namespace EventHub.Application.Me.ChangeMyPassword;

/// <summary>
/// Change the caller's own password (FR36, AD-16). The user is always the caller (<c>ICurrentUser.UserId</c>). Runs in
/// the System Administrator's Platform scope; the credential change itself runs in the Identity scope through
/// <c>IIdentityAccount</c>. Success sets the hash, rotates the security stamp (every other session ends), re-issues
/// this session's cookie with the same <c>iat</c> and writes one <c>user.passwordChanged</c> Audit Entry.
/// </summary>
public sealed record ChangeMyPasswordCommand(string? CurrentPassword, string? NewPassword) : ICommand, IRequirePermission
{
    public static Permission Permission => AppPermissions.MeChangePassword;

    /// <summary>Never prints the passwords (NFR4, AD-22).</summary>
    public override string ToString() => nameof(ChangeMyPasswordCommand);
}
