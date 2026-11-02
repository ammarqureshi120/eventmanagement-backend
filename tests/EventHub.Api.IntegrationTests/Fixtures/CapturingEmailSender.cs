using System.Collections.Concurrent;
using EventHub.Application.Common.Ports;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>
/// Test <see cref="IEmailSender"/>: records every email it is asked to send, optionally after a delay, or throws
/// <see cref="SimulatedSmtpException"/> (whose message holds a fake address and token, which must never reach
/// <c>LastError</c> or the logs) while <see cref="Fail"/> is set.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    /// <summary>Text the simulated failure carries; a leak check looks for it.</summary>
    public const string FailureSecret = "leaky-recipient@example.test token=SIMULATED-SECRET";

    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public bool Fail { get; set; }

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Runs at the start of every send (before any delay or failure); lets a test act mid-batch.</summary>
    public Func<EmailMessage, Task>? OnSend { get; set; }

    public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (OnSend is { } onSend)
        {
            await onSend(message);
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (Fail)
        {
            throw new SimulatedSmtpException(FailureSecret);
        }

        _sent.Enqueue(message);
    }
}

/// <summary>A send failure whose message must never be stored or logged.</summary>
public sealed class SimulatedSmtpException : Exception
{
    public SimulatedSmtpException()
    {
    }

    public SimulatedSmtpException(string message)
        : base(message)
    {
    }

    public SimulatedSmtpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
