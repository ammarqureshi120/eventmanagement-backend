using EventHub.Application.Common.Ports;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>AD-31: tests control "now". Always UTC.</summary>
public sealed class FakeClock : IClock
{
    private DateTime _utcNow = new(2027, 3, 15, 9, 0, 0, DateTimeKind.Utc);

    public DateTime UtcNow
    {
        get => _utcNow;
        set => _utcNow = value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("FakeClock accepts UTC values only.", nameof(value));
    }
}
