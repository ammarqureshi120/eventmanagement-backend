using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Api.Errors;

/// <summary>
/// AD-17: turns any exception into an <see cref="EventHubProblem"/>. Unexpected exceptions become 500
/// <c>server_error</c> with the friendly title only and are logged with the traceId; expected ones
/// (forbidden, not found, validation, bad request) are not logged as errors. Database exceptions are logged
/// by type and SQL error number only, because their messages can echo row values such as an email (AD-22).
/// A client abort writes nothing (status 499); a response that already started is left to the server.
/// </summary>
public sealed partial class EventHubExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<EventHubExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Non-standard "client closed request" status, recorded for aborted requests.</summary>
    public const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = ClientClosedRequest;
            }

            return true;
        }

        var mapped = ProblemMapping.FromException(exception);
        if (mapped.Status >= StatusCodes.Status500InternalServerError)
        {
            var traceId = EventHubProblem.TraceId(httpContext);
            if (IsDatabaseException(exception, out var sqlErrorNumber))
            {
                LogDatabaseFailure(logger, exception.GetType().Name, sqlErrorNumber, traceId, httpContext.Request.Path);
            }
            else
            {
                LogUnhandled(logger, exception, traceId, httpContext.Request.Path);
            }
        }

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        httpContext.Response.StatusCode = mapped.Status;
        var problem = EventHubProblem.Create(httpContext, mapped.Status, mapped.Code, mapped.Errors);

        // Never pass the exception on: no writer may echo its message or stack (NFR4).
        if (await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }))
        {
            return true;
        }

        // The client's Accept header ruled out the default writer; still send the problem body.
        await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }

    private static bool IsDatabaseException(Exception exception, out int? sqlErrorNumber)
    {
        sqlErrorNumber = null;
        var isDatabase = false;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException)
            {
                isDatabase = true;
            }

            if (current is SqlException sql)
            {
                sqlErrorNumber = sql.Number;
                return true;
            }
        }

        return isDatabase;
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Error, Message = "Unhandled exception {TraceId} on {RequestPath}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string traceId, PathString requestPath);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error,
        Message = "Unhandled database exception {ExceptionType} (SQL error {SqlErrorNumber}) {TraceId} on {RequestPath}")]
    private static partial void LogDatabaseFailure(
        ILogger logger, string exceptionType, int? sqlErrorNumber, string traceId, PathString requestPath);
}
