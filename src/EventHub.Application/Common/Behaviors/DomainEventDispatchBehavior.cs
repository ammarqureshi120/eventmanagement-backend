using EventHub.Application.Audit;
using EventHub.Application.Common.Events;
using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Common.Behaviors;

/// <summary>
/// Pipeline step 5 (AD-5, AD-14): after the handler returns, collects domain events from tracked aggregates
/// and application events from <see cref="IApplicationEvents"/>, publishes them (the <c>AuditWriter</c>
/// among the handlers) and repeats until no new events appear. All of it happens before SaveChanges, inside
/// the transaction. More than <see cref="MaxDepth"/> rounds is a bug and fails the request (server_error).
/// </summary>
public sealed class DomainEventDispatchBehavior<TMessage, TResponse>(
    IAppDbContext db,
    IApplicationEvents applicationEvents,
    IPublisher publisher) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public const int MaxDepth = 5;

    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken);

        for (var depth = 0; ; depth++)
        {
            var batch = Collect();
            if (batch.Count == 0)
            {
                return response;
            }

            if (message is IBaseQuery && batch.Exists(notification => notification is IAuditableEvent))
            {
                throw new InvalidOperationException(
                    $"Query {typeof(TMessage).Name} raised an auditable event; audited actions must be commands (AD-14).");
            }

            if (depth >= MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Event dispatch for {typeof(TMessage).Name} did not settle within {MaxDepth} rounds.");
            }

            foreach (var notification in batch)
            {
                await publisher.Publish(notification, cancellationToken);
            }
        }
    }

    private List<INotification> Collect()
    {
        var batch = new List<INotification>();
        foreach (var aggregate in db.GetAggregatesWithEvents())
        {
            var events = aggregate.DomainEvents.ToArray();
            aggregate.ClearDomainEvents();
            batch.AddRange(events.Select(domainEvent => new DomainEventNotification(domainEvent)));
        }

        batch.AddRange(applicationEvents.Drain());
        return batch;
    }
}
