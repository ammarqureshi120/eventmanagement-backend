using System.Diagnostics;
using EventHub.Application.Authorization;
using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using EventHub.Application.Common.Validation;
using Mediator;
using Microsoft.Extensions.Logging;

namespace EventHub.Application.Common.Behaviors;

/// <summary>
/// Pipeline step 1 (AD-5): records message name, actor kind and id, organization, duration and outcome.
/// Never the payload, so no PII, passwords or tokens reach the logs (AD-22).
/// </summary>
public sealed class LoggingBehavior<TMessage, TResponse>(
    ILogger<LoggingBehavior<TMessage, TResponse>> logger,
    ICurrentUser currentUser) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "succeeded";
        try
        {
            return await next(message, cancellationToken);
        }
        catch (Exception exception)
        {
            outcome = Outcome(exception);
            throw;
        }
        finally
        {
            PipelineLog.MessageHandled(
                logger,
                typeof(TMessage).Name,
                currentUser.Kind,
                currentUser.UserId,
                currentUser.OrganizationId,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                outcome);
        }
    }

    private static string Outcome(Exception exception) => exception switch
    {
        InvalidCredentialsException => "invalid_credentials",
        ForbiddenException => "forbidden",
        RequestValidationException => "invalid",
        NotFoundException => "not_found",
        OperationCanceledException => "cancelled",
        _ => "failed",
    };
}

internal static partial class PipelineLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Handled {MessageName} for {ActorKind} {ActorId} in org {OrganizationId} in {ElapsedMs:0.0} ms: {Outcome}")]
    public static partial void MessageHandled(
        ILogger logger,
        string messageName,
        ActorKind actorKind,
        Guid? actorId,
        Guid? organizationId,
        double elapsedMs,
        string outcome);
}
