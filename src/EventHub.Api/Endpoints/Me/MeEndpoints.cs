using EventHub.Application.Me.GetMe;
using EventHub.Contracts.Me;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EventHub.Api.Endpoints.Me;

/// <summary>AD-9: <c>GET /api/me</c> (tag <c>Me</c>). Without a valid session it is 401 <c>session_expired</c>, never a redirect.</summary>
public static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", GetMe)
            .WithTags("Me")
            .WithName("GetMe")
            .WithSummary("Returns the signed-in user's identity, role, Organization, version and nav areas.")
            .RequireAuthorization();

        return api;
    }

    private static async Task<Ok<MeResponse>> GetMe(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMeQuery(), cancellationToken));
}
