using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Infrastructure.Persistence.Scopes;

/// <summary>
/// The four scope ports (AD-7). Each call opens a child DI scope with its own <see cref="AppDbContext"/> and
/// connection, whose session context the RLS interceptor sets on every open. Identity serves login, the
/// stamp validator, reset, accept-invite and the SysAdmin seeder; Platform serves System Administrator work;
/// Tenant serves one Organization; System serves workers and count-only checks.
/// </summary>
public sealed class DbScopeFactory(IServiceScopeFactory scopeFactory)
{
    public DbScope OpenIdentity() => Open(DataScope.Identity);

    public DbScope OpenPlatform() => Open(DataScope.Platform);

    public DbScope OpenTenant(Guid organizationId) => Open(DataScope.Tenant(organizationId));

    public DbScope OpenSystem() => Open(DataScope.System);

    private DbScope Open(DataScope dataScope)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<ScopeAccessor>().Pin(dataScope);
            return new DbScope(scope, dataScope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}

/// <summary>A child DI scope pinned to one <see cref="DataScope"/>. Dispose it to release the context and connection.</summary>
public sealed class DbScope(AsyncServiceScope scope, DataScope dataScope) : IAsyncDisposable, IDisposable
{
    public DataScope DataScope { get; } = dataScope;

    public IServiceProvider Services => scope.ServiceProvider;

    public AppDbContext Db => Services.GetRequiredService<AppDbContext>();

    public ValueTask DisposeAsync() => scope.DisposeAsync();

    public void Dispose() => scope.Dispose();
}
