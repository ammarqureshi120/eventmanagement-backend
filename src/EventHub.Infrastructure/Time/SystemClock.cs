using EventHub.Application.Common.Ports;

namespace EventHub.Infrastructure.Time;

/// <summary>
/// Production <see cref="IClock"/>: the system clock (AD-11, AD-12). It reads <see cref="TimeProvider.System"/>
/// directly; the app-wide <see cref="TimeProvider"/> is <see cref="ClockTimeProvider"/>, which reads this clock.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => TimeProvider.System.GetUtcNow().UtcDateTime;
}
