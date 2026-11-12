using EventHub.Application.Common.Ports;
using EventHub.Application.Common.Validation;
using Mediator;

namespace EventHub.Application.Me.ChangeMyPassword;

/// <summary>
/// Changes the caller's password through <see cref="IIdentityAccount.ChangePasswordAsync"/> (its own Identity-scope
/// transaction: current password checked without counting failures, new hash, stamp rotated, lockout cleared). A
/// wrong current password is a <c>validation</c> error on <c>currentPassword</c> and changes nothing. On success it
/// raises <see cref="UserPasswordChanged"/> and re-issues this session's cookie with the new stamp and the same
/// <c>iat</c> (<see cref="ISessionSignIn.RefreshSignInAsync"/>), so this session stays signed in, within its original
/// 12-hour limit, while every other session ends on its next request (AD-16).
/// </summary>
public sealed class ChangeMyPasswordCommandHandler(
    ICurrentUser currentUser,
    IIdentityAccount accounts,
    IApplicationEvents events,
    ISessionSignIn sessions) : ICommandHandler<ChangeMyPasswordCommand>
{
    /// <summary>The wire key of the current password field.</summary>
    public const string CurrentPasswordKey = "currentPassword";

    public const string WrongCurrentPassword = "That's not your current password. Check it and try again.";

    public async ValueTask<Unit> Handle(ChangeMyPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Checked before the credential change commits, so the cookie refresh after it cannot fail on the session itself
        // (it re-issues the caller's own session with this same iat).
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Changing a password needs a signed-in caller.");
        if (currentUser.SessionIssuedAtUtc is null)
        {
            throw new InvalidOperationException("Changing a password needs the caller's session sign-in time.");
        }

        var account = await accounts.ChangePasswordAsync(userId, command.CurrentPassword!, command.NewPassword!, cancellationToken);
        if (account is null)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [CurrentPasswordKey] = [WrongCurrentPassword],
            });
        }

        events.Raise(new UserPasswordChanged(userId));
        await sessions.RefreshSignInAsync(account, cancellationToken);
        return Unit.Value;
    }
}
