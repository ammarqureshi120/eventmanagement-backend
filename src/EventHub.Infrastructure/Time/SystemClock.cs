using EventHub.Application.Common.Ports;

namespace EventHub.Infrastructure.Time;

/// <summary>Production <see cref="IClock"/>: the system clock (AD-11, AD-12).</summary>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
