using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Auth.Login;

/// <summary>
/// Verifies the password through <see cref="IIdentityAccount"/> (constant work for every failure), raises
/// <see cref="UserSignedIn"/> with the user as subject actor, then stages the session cookie. Any failure is the
/// same <see cref="InvalidCredentialsException"/>: no cookie, no audit entry.
/// </summary>
public sealed class LoginCommandHandler(IIdentityAccount accounts, IApplicationEvents events, ISessionSignIn sessions)
    : ICommandHandler<LoginCommand>
{
    public async ValueTask<Unit> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var account = await accounts.VerifyPasswordAsync(command.Email!, command.Password!, cancellationToken)
                      ?? throw new InvalidCredentialsException();

        events.Raise(new UserSignedIn(account));
        await sessions.SignInAsync(account, cancellationToken);
        return Unit.Value;
    }
}
