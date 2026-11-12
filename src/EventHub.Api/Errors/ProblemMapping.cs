using EventHub.Api.Hosting;
using EventHub.Application.Authorization;
using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Validation;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Api.Errors;

/// <summary>
/// AD-17 mapping, defined once: exceptions and bare status codes to (status, code). Story 1.3 maps
/// <c>validation</c>, <c>forbidden</c>, <c>not_found</c> and <c>server_error</c>; Story 1.4 adds
/// <c>invalid_credentials</c> and <c>session_expired</c> (a bare 401: the cookie challenge); Story 1.5 adds
/// <c>account_disabled</c> (the exception, or a bare 401 after the session validator marked the user inactive);
/// Story 1.6 adds <c>token_invalid</c> (400, optional <c>reason</c> <c>expired</c> | <c>used</c>); Story 1.7 adds
/// <c>concurrency_conflict</c> (409: a stale <c>version</c>, or a save that lost the race on the concurrency token,
/// AD-24); later stories add theirs here.
/// </summary>
public static class ProblemMapping
{
    public sealed record Mapped(
        int Status, string Code, IReadOnlyDictionary<string, string[]>? Errors = null, string? Reason = null);

    public static Mapped FromException(Exception exception) => exception switch
    {
        InvalidCredentialsException => new Mapped(StatusCodes.Status401Unauthorized, EventHubProblem.Codes.InvalidCredentials),
        AccountDisabledException => new Mapped(StatusCodes.Status401Unauthorized, EventHubProblem.Codes.AccountDisabled),
        TokenInvalidException invalidToken => new Mapped(
            StatusCodes.Status400BadRequest, EventHubProblem.Codes.TokenInvalid, Reason: ReasonOf(invalidToken.Reason)),
        ForbiddenException => new Mapped(StatusCodes.Status403Forbidden, EventHubProblem.Codes.Forbidden),
        NotFoundException => new Mapped(StatusCodes.Status404NotFound, EventHubProblem.Codes.NotFound),
        ConcurrencyConflictException or DbUpdateConcurrencyException =>
            new Mapped(StatusCodes.Status409Conflict, EventHubProblem.Codes.ConcurrencyConflict),
        RequestValidationException invalid => new Mapped(
            StatusCodes.Status400BadRequest, EventHubProblem.Codes.Validation, invalid.Errors),

        // Malformed JSON, wrong content type, oversized body: thrown by minimal-API binding.
        BadHttpRequestException badRequest when badRequest.StatusCode is >= 400 and < 500 =>
            new Mapped(badRequest.StatusCode, CodeForStatus(badRequest.StatusCode)),
        _ => new Mapped(StatusCodes.Status500InternalServerError, EventHubProblem.Codes.ServerError),
    };

    /// <summary>The wire value of a <c>token_invalid</c> reason: <c>expired</c> | <c>used</c>, or none.</summary>
    public static string? ReasonOf(TokenInvalidReason? reason) => reason switch
    {
        TokenInvalidReason.Expired => "expired",
        TokenInvalidReason.Used => "used",
        _ => null,
    };

    /// <summary>
    /// A bare status for this request: a 401 after the session validator rejected an inactive user
    /// (<see cref="SessionStampValidator.DisabledItemKey"/>) is <c>account_disabled</c>; everything else as
    /// <see cref="CodeForStatus(int)"/>.
    /// </summary>
    public static string CodeForStatus(HttpContext context, int status)
    {
        ArgumentNullException.ThrowIfNull(context);
        return status == StatusCodes.Status401Unauthorized && context.Items.ContainsKey(SessionStampValidator.DisabledItemKey)
            ? EventHubProblem.Codes.AccountDisabled
            : CodeForStatus(status);
    }

    /// <summary>401 → session_expired, 400/415 → validation, 403 → forbidden, 404/405 → not_found, 409 → concurrency_conflict, ≥500 → server_error; other 4xx → validation.</summary>
    public static string CodeForStatus(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => EventHubProblem.Codes.SessionExpired,
        StatusCodes.Status403Forbidden => EventHubProblem.Codes.Forbidden,
        StatusCodes.Status404NotFound or StatusCodes.Status405MethodNotAllowed => EventHubProblem.Codes.NotFound,
        StatusCodes.Status409Conflict => EventHubProblem.Codes.ConcurrencyConflict,
        >= 500 => EventHubProblem.Codes.ServerError,
        _ => EventHubProblem.Codes.Validation,
    };
}
