using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Auth.Logout;

/// <summary>Raises <see cref="UserSignedOut"/>, then stages the cookie deletion (AD-16, AD-14).</summary>
public sealed class LogoutCommandHandler(ICurrentUser currentUser, IApplicationEvents events, ISessionSignIn sessions)
    : ICommandHandler<LogoutCommand, SignedOutUser>
{
    public async ValueTask<SignedOutUser> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
                     ?? throw new InvalidOperationException("Logout needs a signed-in caller.");

        events.Raise(new UserSignedOut(userId));
        await sessions.SignOutAsync(cancellationToken);
        return new SignedOutUser(userId);
    }
}
