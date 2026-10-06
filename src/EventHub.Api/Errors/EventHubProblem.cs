using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Api.Errors;

/// <summary>
/// AD-17: every non-2xx is an RFC 9457 ProblemDetails with a stable <c>code</c> and the W3C <c>traceId</c>,
/// a friendly <c>title</c> and never exception text, stack traces or PII (NFR4, NFR12). The OpenAPI schema
/// arrives with the first endpoint that documents error responses.
/// </summary>
public static class EventHubProblem
{
    public const string TypeBase = "https://eventhub.dev/errors/";
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";
    public const string ErrorsKey = "errors";

    public static class Codes
    {
        public const string Validation = "validation";
        public const string InvalidCredentials = "invalid_credentials";
        public const string SessionExpired = "session_expired";
        public const string Forbidden = "forbidden";
        public const string NotFound = "not_found";
        public const string ServerError = "server_error";
    }

    public static ProblemDetails Create(
        HttpContext context, int status, string code, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = new ProblemDetails
        {
            Status = status,
            Type = TypeBase + code,
            Title = Title(code),
        };
        problem.Extensions[CodeKey] = code;
        problem.Extensions[TraceIdKey] = TraceId(context);
        if (errors is { Count: > 0 })
        {
            problem.Extensions[ErrorsKey] = errors;
        }

        return problem;
    }

    /// <summary>
    /// <c>AddProblemDetails</c> hook: every ProblemDetails the framework writes (status-code pages, the
    /// exception handler) gets the EventHub shape. A code already set by <see cref="ProblemMapping"/> wins.
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        var code = problem.Extensions.TryGetValue(CodeKey, out var existing) && existing is string known
            ? known
            : ProblemMapping.CodeForStatus(status);

        problem.Status = status;
        problem.Type = TypeBase + code;
        problem.Title = Title(code);
        problem.Detail = null;
        problem.Instance = null;
        problem.Extensions.Remove("exception");
        problem.Extensions[CodeKey] = code;
        problem.Extensions[TraceIdKey] = TraceId(context.HttpContext);
    }

    /// <summary>The W3C trace id (32 hex chars) of the current activity, else the request's TraceIdentifier.</summary>
    public static string TraceId(HttpContext context) =>
        Activity.Current is { } activity && activity.TraceId != default
            ? activity.TraceId.ToHexString()
            : context.TraceIdentifier;

    private static string Title(string code) => code switch
    {
        Codes.Validation => "Some fields need a look.",
        Codes.InvalidCredentials => "Email or password is incorrect.",
        Codes.SessionExpired => "Your session has ended. Please sign in again.",
        Codes.Forbidden => "You don't have access to do that.",
        Codes.NotFound => "We couldn't find that.",
        _ => "Something went wrong on our side. Please try again.",
    };
}
