using EventHub.Application.Me.ChangeMyPassword;
using EventHub.Application.Me.GetMe;
using EventHub.Application.Me.UpdateMyProfile;
using EventHub.Contracts.Me;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EventHub.Api.Endpoints.Me;

/// <summary>
/// AD-9: <c>GET /api/me</c> (tag <c>Me</c>). Without a valid session it is 401 <c>session_expired</c>, never a redirect.
/// Story 1.7 (FR36): <c>PUT /api/me/profile</c> and <c>POST /api/me/change-password</c>, both 204 and antiforgery-checked
/// by the <c>/api</c> group filter. Bind, dispatch, map.
/// </summary>
public static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", GetMe)
            .WithTags("Me")
            .WithName("GetMe")
            .WithSummary("Returns the signed-in user's identity, role, Organization, version and nav areas.")
            .RequireAuthorization();

        api.MapPut("/me/profile", UpdateMyProfile)
            .WithTags("Me")
            .WithName("UpdateMyProfile")
            .WithSummary("Edits the signed-in user's first name, last name and phone. A stale version is 409 concurrency_conflict.")
            .RequireAuthorization();

        api.MapPost("/me/change-password", ChangeMyPassword)
            .WithTags("Me")
            .WithName("ChangeMyPassword")
            .WithSummary("Changes the signed-in user's password: this session stays signed in, every other session ends.")
            .RequireAuthorization();

        return api;
    }

    private static async Task<Ok<MeResponse>> GetMe(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMeQuery(), cancellationToken));

    private static async Task<NoContent> UpdateMyProfile(UpdateMyProfileRequest request, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(
            new UpdateMyProfileCommand(request.FirstName, request.LastName, request.Phone, request.Version), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ChangeMyPassword(ChangeMyPasswordRequest request, ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ChangeMyPasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken);
        return TypedResults.NoContent();
    }
}
