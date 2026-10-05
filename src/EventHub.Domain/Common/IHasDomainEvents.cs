namespace EventHub.Domain.Common;

/// <summary>Marker for a domain event raised by an aggregate (AD-5). Domain events stay BCL-only.</summary>
public interface IDomainEvent;

/// <summary>
/// Aggregates that raise domain events. The domain-event dispatch behavior collects them from tracked
/// aggregates, clears them and runs their handlers inside the command transaction (AD-5).
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
