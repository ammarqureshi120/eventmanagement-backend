using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Auth.Login;

/// <summary>
/// Verifies the password through <see cref="IIdentityAccount"/> (constant work for every failure, lockout after 5
/// failures), raises <see cref="UserSignedIn"/> with the user as subject actor, then stages the session cookie.
/// Any failure (wrong password, unknown email, no password, locked) is the same
/// <see cref="InvalidCredentialsException"/>; a correct password for a user who is not Active is
/// <see cref="AccountDisabledException"/>. Neither issues a cookie or writes an audit entry.
/// </summary>
public sealed class LoginCommandHandler(IIdentityAccount accounts, IApplicationEvents events, ISessionSignIn sessions)
    : ICommandHandler<LoginCommand>
{
    public async ValueTask<Unit> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var check = await accounts.VerifyPasswordAsync(command.Email!, command.Password!, cancellationToken);
        var account = check switch
        {
            { Outcome: PasswordCheckOutcome.Succeeded, Account: { } succeeded } => succeeded,
            { Outcome: PasswordCheckOutcome.Disabled } => throw new AccountDisabledException(),
            _ => throw new InvalidCredentialsException(),
        };

        events.Raise(new UserSignedIn(account));
        await sessions.SignInAsync(account, cancellationToken);
        return Unit.Value;
    }
}
