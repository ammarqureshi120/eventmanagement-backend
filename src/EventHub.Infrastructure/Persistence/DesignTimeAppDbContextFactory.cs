using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EventHub.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> (migrations add, has-pending-model-changes) so the tools never boot the API host.
/// The connection string is only used by commands that talk to a database (for example <c>database update</c>);
/// override it with <c>EVENTHUB_DESIGN_SQL</c>.
/// </summary>
public sealed class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("EVENTHUB_DESIGN_SQL")
                               ?? "Server=localhost;Database=EventHub;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        return new AppDbContext(options, new ScopeAccessor());
    }
}
