using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventHub.Infrastructure.Outbox;

/// <summary>
/// AD-15: the background outbox relay, on every API replica (claims are leased, so replicas never double-send). It
/// polls every <see cref="OutboxOptions.PollIntervalSeconds"/> and drains full batches back to back. With
/// <c>EventHub:Outbox:Enabled=false</c> it does nothing. It is infrastructure, not a Mediator use case, and writes no
/// audit. A failed pass is logged by exception type only and retried on the next tick.
/// </summary>
public sealed partial class OutboxRelay(
    OutboxProcessor processor,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, settings.PollIntervalSeconds)));
        try
        {
            do
            {
                try
                {
                    while (await processor.ProcessBatchAsync(stoppingToken) >= Math.Max(1, settings.BatchSize))
                    {
                        // A full batch: more may be due right now.
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    LogPassFailed(logger, exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    [LoggerMessage(EventId = 4010, Level = LogLevel.Information, Message = "Outbox relay disabled (EventHub:Outbox:Enabled is false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Error, Message = "Outbox relay pass failed: {ErrorType}")]
    private static partial void LogPassFailed(ILogger logger, string errorType);
}
