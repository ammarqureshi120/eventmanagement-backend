using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using EventHub.Application.Common.Validation;
using EventHub.Contracts.Auth;
using Mediator;

namespace EventHub.Application.Auth.ResetPassword;

/// <summary>
/// AD-17 order: the token first (<see cref="TokenInvalidException"/>), then <see cref="PasswordPolicy"/> as a
/// <c>validation</c> failure on <c>newPassword</c> (nothing saved, the token stays usable), then the reset itself
/// through <see cref="IIdentityAccount"/> (single use, stamp rotated, lockout cleared), then
/// <see cref="UserPasswordResetCompleted"/> with the user as subject actor (AD-14). Returns the account's email.
/// </summary>
public sealed class ResetPasswordCommandHandler(IIdentityAccount accounts, IApplicationEvents events)
    : ICommandHandler<ResetPasswordCommand, ResetPasswordResponse>
{
    /// <summary>The wire key of the new password field.</summary>
    public const string NewPasswordKey = "newPassword";

    public async ValueTask<ResetPasswordResponse> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var token = command.Token!;

        var check = await accounts.CheckResetTokenAsync(token, cancellationToken);
        if (!check.Succeeded)
        {
            throw new TokenInvalidException(check.Reason);
        }

        var broken = PasswordPolicy.Check(command.NewPassword);
        if (broken.Count > 0)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [NewPasswordKey] = [.. broken],
            });
        }

        // Re-checked atomically inside the reset: a concurrent submit of the same token loses here.
        var reset = await accounts.ResetPasswordAsync(token, command.NewPassword!, cancellationToken);
        if (!reset.Succeeded)
        {
            throw new TokenInvalidException(reset.Reason);
        }

        // A success always carries the account and its email (PasswordResetResult.Valid), so nothing here can fail
        // after the reset committed.
        events.Raise(new UserPasswordResetCompleted(reset.Account));
        return new ResetPasswordResponse(reset.Email);
    }
}
