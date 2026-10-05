using Serilog.Core;
using Serilog.Events;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>Captures every Serilog event the API writes (registered as an <see cref="ILogEventSink"/> service).</summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];

    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_events)
        {
            _events.Add(logEvent);
        }
    }

    /// <summary>Message, every property and any exception, rendered as text.</summary>
    public static string Render(LogEvent logEvent) =>
        string.Join(
            " | ",
            new[] { logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture), logEvent.Exception?.ToString() ?? string.Empty }
                .Concat(logEvent.Properties.Select(p => $"{p.Key}={p.Value}")));

    public IReadOnlyList<string> RenderAll() => Events.Select(Render).ToList();
}
