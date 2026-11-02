using EventHub.Application.Authorization;
using EventHub.Application.Common.Tenancy;
using Mediator;

namespace EventHub.Application.Auth.ForgotPassword;

/// <summary>
/// Request a password-reset link by email (FR4, FR37, AD-16). Anonymous, in the Identity scope (AD-7). The answer is
/// always the same 202 whatever the account state; only an Active account gets a token and an email (sent later by
/// the outbox relay, so the response time does not reveal the account either).
/// </summary>
public sealed record ForgotPasswordCommand(string? Email) : ICommand, IRequirePermission, IIdentityScoped
{
    public static Permission Permission => AppPermissions.AuthForgotPassword;

    /// <summary>Never prints the email (AD-22).</summary>
    public override string ToString() => nameof(ForgotPasswordCommand);
}
