using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Common.Behaviors;

/// <summary>
/// Pipeline step 4 (AD-5), commands only: opens a ReadCommitted transaction (RCSI on), runs domain-event
/// dispatch and the handler, saves, commits. Any exception rolls back. Queries pass straight through, and a
/// command dispatched inside an open transaction joins it.
/// </summary>
public sealed class TransactionBehavior<TMessage, TResponse>(IAppDbContext db) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        if (db.HasActiveTransaction)
        {
            return await next(message, cancellationToken);
        }

        if (message is IBaseQuery)
        {
            var result = await next(message, cancellationToken);
            if (db.HasPendingChanges)
            {
                // A query never saves, so anything it changed (including an audit entry) would be lost silently.
                throw new InvalidOperationException($"Query {typeof(TMessage).Name} left unsaved changes; queries never mutate (AD-6).");
            }

            return result;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var response = await next(message, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
