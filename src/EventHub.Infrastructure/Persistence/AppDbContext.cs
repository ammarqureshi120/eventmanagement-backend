using System.Data;
using EventHub.Application.Common.Ports;
using EventHub.Domain.Audit;
using EventHub.Domain.Common;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence.Configurations;
using EventHub.Infrastructure.Persistence.Conventions;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventHub.Infrastructure.Persistence;

/// <summary>
/// The EF Core context (AD-30). One instance per DI scope, so each data scope (AD-7) has its own context and
/// connection; the RLS interceptor sets the session context from <see cref="ScopeAccessor"/> on every open.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ScopeAccessor scopeAccessor)
    : DbContext(options), IAppDbContext, ITenantFilterSource
{
    public DbSet<User> Users => Set<User>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DataScope DataScope => scopeAccessor.Current;

    /// <inheritdoc />
    public bool IsSystemScope => scopeAccessor.Current.Kind == ScopeKind.System;

    /// <inheritdoc />
    public Guid CurrentTenantOrganizationId => scopeAccessor.Current switch
    {
        { Kind: ScopeKind.Tenant, OrganizationId: { } organizationId } => organizationId,
        { Kind: ScopeKind.System } => Guid.Empty, // unused: the System scope passes the filter (fn_tenant allows it too)
        _ => throw new InvalidOperationException(
            "A tenant-owned query ran without an Organization in scope (AD-7 fails closed)."),
    };

    public bool HasActiveTransaction => Database.CurrentTransaction is not null;

    public bool HasPendingChanges => ChangeTracker.HasChanges();

    void IAppDbContext.Add<TEntity>(TEntity entity) => Add(entity);

    public IReadOnlyList<IHasDomainEvents> GetAggregatesWithEvents() =>
        ChangeTracker.Entries<IHasDomainEvents>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new EfTransaction(await Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // AD-12: every DateTime is stored as UTC datetime2 and materialized as DateTimeKind.Utc.
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>()
            .HaveColumnType("datetime2");
        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableUtcDateTimeConverter>()
            .HaveColumnType("datetime2");

        // AD-19: enums are stored as varchar names.
        configurationBuilder.Properties<Enum>()
            .HaveConversion<string>()
            .AreUnicode(false)
            .HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // AD-7 layer 2: every tenant-owned entity gets the named "Tenant" filter.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(type => typeof(ITenantOwned).IsAssignableFrom(type.ClrType))
                     .ToList())
        {
            modelBuilder.Entity(entityType.ClrType).HasTenantFilter(this);
        }
    }

    /// <summary>AD-14: Audit Entries are append-only; no code path may update or delete them.</summary>
    private void GuardAppendOnly()
    {
        if (ChangeTracker.Entries<AuditEntry>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit Entries are append-only (AD-14).");
        }
    }

    private sealed class EfTransaction(IDbContextTransaction transaction) : IAppTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
