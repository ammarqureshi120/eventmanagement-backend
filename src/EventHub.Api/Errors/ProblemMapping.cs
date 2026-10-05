using EventHub.Application.Authorization;
using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Validation;

namespace EventHub.Api.Errors;

/// <summary>
/// AD-17 mapping, defined once: exceptions and bare status codes to (status, code). Story 1.3 maps
/// <c>validation</c>, <c>forbidden</c>, <c>not_found</c> and <c>server_error</c>; later stories add their codes here.
/// </summary>
public static class ProblemMapping
{
    public sealed record Mapped(int Status, string Code, IReadOnlyDictionary<string, string[]>? Errors = null);

    public static Mapped FromException(Exception exception) => exception switch
    {
        ForbiddenException => new Mapped(StatusCodes.Status403Forbidden, EventHubProblem.Codes.Forbidden),
        NotFoundException => new Mapped(StatusCodes.Status404NotFound, EventHubProblem.Codes.NotFound),
        RequestValidationException invalid => new Mapped(
            StatusCodes.Status400BadRequest, EventHubProblem.Codes.Validation, invalid.Errors),

        // Malformed JSON, wrong content type, oversized body: thrown by minimal-API binding.
        BadHttpRequestException badRequest when badRequest.StatusCode is >= 400 and < 500 =>
            new Mapped(badRequest.StatusCode, CodeForStatus(badRequest.StatusCode)),
        _ => new Mapped(StatusCodes.Status500InternalServerError, EventHubProblem.Codes.ServerError),
    };

    /// <summary>400/415 → validation, 403 → forbidden, 404/405 → not_found, ≥500 → server_error; other 4xx → validation.</summary>
    public static string CodeForStatus(int status) => status switch
    {
        StatusCodes.Status403Forbidden => EventHubProblem.Codes.Forbidden,
        StatusCodes.Status404NotFound or StatusCodes.Status405MethodNotAllowed => EventHubProblem.Codes.NotFound,
        >= 500 => EventHubProblem.Codes.ServerError,
        _ => EventHubProblem.Codes.Validation,
    };
}
