using EventHub.Api.Errors;
using Microsoft.AspNetCore.Antiforgery;

namespace EventHub.Api.Hosting;

/// <summary>
/// AD-16 / NFR5: the <c>/api</c> group endpoint filter. Every POST, PUT, PATCH and DELETE (login included) must carry
/// an <c>X-XSRF-TOKEN</c> header matching the antiforgery cookie; otherwise the request ends here with the Story 1.3
/// 400 <c>validation</c> problem and no Mediator behavior or handler runs. Safe verbs (the token-issuing
/// <c>GET /api/auth/antiforgery</c> among them) pass through. Tokens are bound to the signed-in user, so the SPA
/// refreshes its token after login and logout.
/// </summary>
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        if (!IsUnsafe(httpContext.Request.Method))
        {
            return await next(context);
        }

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return TypedResults.Problem(EventHubProblem.Create(
                httpContext, StatusCodes.Status400BadRequest, EventHubProblem.Codes.Validation));
        }

        return await next(context);
    }

    private static bool IsUnsafe(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
