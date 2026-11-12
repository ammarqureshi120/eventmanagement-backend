using EventHub.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EventHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// AD-24: bumps <see cref="IVersioned.Version"/> by one on every Modified versioned entity at SaveChanges. EF keeps the
/// loaded value as the concurrency token's original value, so the UPDATE matches only the version that was read and a
/// stale write throws <see cref="DbUpdateConcurrencyException"/> (409 <c>concurrency_conflict</c>). A Version that is
/// already modified is left alone: the Identity <c>UserStore.UpdateAsync</c> bumps it itself, and a second bump would
/// skip a number.
/// </summary>
public sealed class VersionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Bump(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Bump(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    internal static void Bump(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var version = entry.Property(nameof(IVersioned.Version));
            if (!version.IsModified)
            {
                version.CurrentValue = (int)version.CurrentValue! + 1;
            }
        }
    }
}
