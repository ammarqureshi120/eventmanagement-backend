using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EventHub.Api.Endpoints.Auth;

public static class AntiforgeryEndpoints
{
    /// <summary>Header the SPA sends on POST/PUT/PATCH/DELETE (AD-16).</summary>
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>JS-readable cookie carrying the request token (AD-16).</summary>
    public const string CookieName = "XSRF-TOKEN";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapGet("/antiforgery", IssueToken)
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issues the XSRF-TOKEN cookie the SPA echoes in the X-XSRF-TOKEN header.");

        return api;
    }

    private static NoContent IssueToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(CookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
        });
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.NoContent();
    }
}
