using Mediator;

namespace EventHub.Application.Common.Ports;

/// <summary>
/// Raises application events from a handler (AD-5, AD-14): actions that change no aggregate (exports,
/// sign-in/out, reset completed) raise an <c>IAuditableEvent</c> here. The domain-event dispatch behavior
/// drains the queue after the handler returns, before SaveChanges, inside the transaction.
/// </summary>
public interface IApplicationEvents
{
    void Raise(INotification applicationEvent);

    /// <summary>Removes and returns every event raised so far.</summary>
    IReadOnlyList<INotification> Drain();
}
