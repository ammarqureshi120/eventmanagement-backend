using EventHub.Domain.Common;

namespace EventHub.Application.Common.Ports;

/// <summary>
/// The write-side unit of work of the request scope (AD-5, AD-6). EF-free on purpose: Application never sees
/// EF Core types (AD-1). Feature stories add their aggregate access here as they arrive.
/// </summary>
public interface IAppDbContext
{
    /// <summary>True while a transaction opened by <see cref="BeginTransactionAsync"/> is active.</summary>
    bool HasActiveTransaction { get; }

    /// <summary>True when tracked entities have unsaved changes.</summary>
    bool HasPendingChanges { get; }

    /// <summary>Tracks a new aggregate or append-only record for insert on the next save.</summary>
    void Add<TEntity>(TEntity entity)
        where TEntity : class;

    /// <summary>Tracked aggregates that currently hold domain events.</summary>
    IReadOnlyList<IHasDomainEvents> GetAggregatesWithEvents();

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a ReadCommitted transaction (RCSI is on, AD-5). No handler picks its own isolation level.</summary>
    Task<IAppTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>A unit-of-work transaction; disposing without <see cref="CommitAsync"/> rolls back.</summary>
public interface IAppTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
