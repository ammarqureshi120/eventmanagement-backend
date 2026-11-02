using EventHub.Application.Common.Ports;

namespace EventHub.Infrastructure.Time;

/// <summary>
/// The app-wide <see cref="TimeProvider"/>: "now" comes from <see cref="IClock"/> (AD-12, AD-31). Cookie expiry,
/// the session validator and lockout all read it, so a test that advances <c>FakeClock</c> moves them together.
/// Timestamps and timers stay on the system provider (they measure elapsed time, not wall-clock time).
/// </summary>
public sealed class ClockTimeProvider(IClock clock) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() =>
        new(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc), TimeSpan.Zero);
}
