using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Auth.ForgotPassword;

/// <summary>Hands the trimmed email to <see cref="IUserTokenIssuer"/>, which decides silently whether to issue (AD-15).</summary>
public sealed class ForgotPasswordCommandHandler(IUserTokenIssuer issuer) : ICommandHandler<ForgotPasswordCommand>
{
    public async ValueTask<Unit> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await issuer.IssuePasswordResetAsync(command.Email!.Trim(), cancellationToken);
        return Unit.Value;
    }
}
