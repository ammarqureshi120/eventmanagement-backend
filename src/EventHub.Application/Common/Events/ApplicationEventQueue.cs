using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Common.Events;

/// <summary>Scoped <see cref="IApplicationEvents"/>: one queue per request/use-case scope.</summary>
public sealed class ApplicationEventQueue : IApplicationEvents
{
    private readonly List<INotification> _events = [];

    public void Raise(INotification applicationEvent)
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);
        _events.Add(applicationEvent);
    }

    public IReadOnlyList<INotification> Drain()
    {
        var drained = _events.ToArray();
        _events.Clear();
        return drained;
    }
}
