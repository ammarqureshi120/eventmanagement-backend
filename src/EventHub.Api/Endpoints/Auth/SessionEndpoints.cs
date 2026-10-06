using EventHub.Application.Auth.Login;
using EventHub.Application.Auth.Logout;
using EventHub.Application.Common.Ports;
using EventHub.Contracts.Auth;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EventHub.Api.Endpoints.Auth;

/// <summary>
/// AD-16: <c>POST /api/auth/login</c> and <c>POST /api/auth/logout</c> (tag <c>Auth</c>). Bind, dispatch, map; both
/// are antiforgery-checked by the <c>/api</c> group filter.
/// </summary>
public static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Signs in with email and password and issues the .EventHub.Session cookie.");

        auth.MapPost("/logout", Logout)
            .WithName("Logout")
            .WithSummary("Signs out: clears the session cookie and ends every session of the user on every device.")
            .RequireAuthorization();

        return auth;
    }

    private static async Task<Ok> Login(LoginRequest request, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new LoginCommand(request.Email, request.Password), cancellationToken);
        return TypedResults.Ok();
    }

    /// <summary>
    /// The command commits <c>user.signedOut</c> and stages the cookie deletion; only then is the security stamp
    /// rotated (own Identity-scope connection), so a replayed old cookie is rejected everywhere. If rotation fails
    /// the request ends in 500 with the cookie still cleared, and the old cookie stays valid until the next
    /// successful logout (accepted, documented in the 1.4a spec). Rotation does not use the request token: once the
    /// sign-out is committed, a client abort must not skip it, so it gets its own bounded timeout instead.
    /// </summary>
    private static async Task<NoContent> Logout(ISender sender, IIdentityAccount accounts, CancellationToken cancellationToken)
    {
        var signedOut = await sender.Send(new LogoutCommand(), cancellationToken);
        using var rotation = new CancellationTokenSource(RotationTimeout);
        await accounts.RotateSecurityStampAsync(signedOut.UserId, rotation.Token);
        return TypedResults.NoContent();
    }

    /// <summary>Upper bound for the post-commit stamp rotation, independent of the client connection.</summary>
    private static readonly TimeSpan RotationTimeout = TimeSpan.FromSeconds(30);
}
