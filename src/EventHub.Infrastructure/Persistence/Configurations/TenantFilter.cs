using System.Linq.Expressions;
using EventHub.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Infrastructure.Persistence.Configurations;

/// <summary>A DbContext that can name the Organization in scope for the "Tenant" query filter.</summary>
public interface ITenantFilterSource
{
    /// <summary>True in the System scope, which may read every Organization's rows (as <c>sec.fn_tenant</c> allows).</summary>
    bool IsSystemScope { get; }

    /// <summary>
    /// The Organization of the current tenant scope; throws when none is in scope (fail closed). In the System
    /// scope it must not throw (any value; <see cref="IsSystemScope"/> decides).
    /// </summary>
    Guid CurrentTenantOrganizationId { get; }
}

/// <summary>
/// AD-7 layer 2: the EF Core 10 named query filter <c>"Tenant"</c> for tenant-owned entities. The filter reads
/// <see cref="ITenantFilterSource.CurrentTenantOrganizationId"/> from the context instance per query, so a
/// tenant-owned query with no Organization in scope throws instead of returning rows. The System scope passes.
/// </summary>
public static class TenantFilter
{
    public const string Name = "Tenant";

    public static EntityTypeBuilder HasTenantFilter<TContext>(this EntityTypeBuilder builder, TContext context)
        where TContext : DbContext, ITenantFilterSource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(context);

        var clrType = builder.Metadata.ClrType;
        if (!typeof(ITenantOwned).IsAssignableFrom(clrType))
        {
            throw new InvalidOperationException($"{clrType.Name} is not tenant-owned (ITenantOwned).");
        }

        var entity = Expression.Parameter(clrType, "entity");
        var source = Expression.Constant(context);
        var body = Expression.OrElse(
            Expression.Property(source, nameof(ITenantFilterSource.IsSystemScope)),
            Expression.Equal(
                Expression.Property(entity, nameof(ITenantOwned.OrganizationId)),
                Expression.Property(source, nameof(ITenantFilterSource.CurrentTenantOrganizationId))));

        builder.HasQueryFilter(Name, Expression.Lambda(body, entity));
        return builder;
    }
}
