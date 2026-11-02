namespace EventHub.Infrastructure.Outbox;

/// <summary><c>EventHub:Outbox:*</c> relay settings (AD-15). Defaults suit every environment; tests switch the relay off.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "EventHub:Outbox";

    /// <summary>False: the background relay starts and does nothing (the processor can still be driven directly).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Rows claimed per batch.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>How long a claim keeps other relays away; longer than <see cref="SendTimeoutSeconds"/>.</summary>
    public int LeaseSeconds { get; set; } = 60;

    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Upper bound for one SMTP send.</summary>
    public int SendTimeoutSeconds { get; set; } = 30;

    /// <summary>After this many failed attempts a row is dead-lettered (never claimed again).</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>The first retry delay; it doubles per attempt up to <see cref="MaxBackoff"/>.</summary>
    public static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    /// <summary>The delay after the <paramref name="attempts"/>-th failure: 30 s × 2^(n−1), capped at 1 h.</summary>
    public static TimeSpan Backoff(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var exponent = Math.Min(attempts - 1, 16);
        var delay = BaseBackoff * Math.Pow(2, exponent);
        return delay < MaxBackoff ? delay : MaxBackoff;
    }
}
