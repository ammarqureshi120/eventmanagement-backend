using EventHub.Application.Auth.ForgotPassword;
using EventHub.Application.Auth.ResetPassword;
using EventHub.Contracts.Auth;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EventHub.Api.Endpoints.Auth;

/// <summary>
/// FR4 / AD-16: <c>POST /api/auth/forgot-password</c> and <c>POST /api/auth/reset-password</c> (tag <c>Auth</c>). Bind,
/// dispatch, map. Both are anonymous and antiforgery-checked by the <c>/api</c> group filter; the reset token travels
/// only in the POST body.
/// </summary>
public static class PasswordResetEndpoints
{
    public static RouteGroupBuilder MapPasswordResetEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/forgot-password", ForgotPassword)
            .WithName("ForgotPassword")
            .WithSummary("Requests a password-reset email. Always 202 with no body, whether or not the account exists.");

        auth.MapPost("/reset-password", ResetPassword)
            .WithName("ResetPassword")
            .WithSummary("Sets a new password with the emailed reset token, ends every session of the user and returns the account email.");

        return auth;
    }

    private static async Task<Accepted> ForgotPassword(ForgotPasswordRequest request, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ForgotPasswordCommand(request.Email), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Ok<ResetPasswordResponse>> ResetPassword(ResetPasswordRequest request, ISender sender, CancellationToken cancellationToken)
    {
        var response = await sender.Send(new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken);
        return TypedResults.Ok(response);
    }
}
