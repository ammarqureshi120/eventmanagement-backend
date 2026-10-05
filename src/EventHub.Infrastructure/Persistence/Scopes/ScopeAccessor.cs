using EventHub.Application.Common.Ports;

namespace EventHub.Infrastructure.Persistence.Scopes;

/// <summary>
/// Scoped holder of the DI scope's <see cref="DataScope"/> (AD-7). A request scope derives it from
/// <see cref="ITenantContext"/>; a child scope opened by <see cref="DbScopeFactory"/> pins it explicitly,
/// once, before its DbContext opens a connection.
/// </summary>
public sealed class ScopeAccessor(ITenantContext? tenantContext = null)
{
    private DataScope? _pinned;

    public DataScope Current => _pinned ?? (tenantContext is null ? DataScope.None : DataScope.From(tenantContext));

    internal void Pin(DataScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (_pinned is not null)
        {
            throw new InvalidOperationException("The data scope of a DI scope can be set only once.");
        }

        _pinned = scope;
    }
}
