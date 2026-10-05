using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventHub.Infrastructure.Persistence;

/// <summary><c>EventHub:Database:*</c> settings.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "EventHub:Database";

    /// <summary>
    /// Apply pending migrations when the API starts. Unset means: only in Development or Testing; deployments
    /// run the migration bundle first (AD-30).
    /// </summary>
    public bool? MigrateOnStartup { get; set; }

    public bool ShouldMigrate(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return MigrateOnStartup ?? (environment.IsDevelopment() || environment.IsEnvironment("Testing"));
    }
}

/// <summary>
/// Applies pending EF migrations at startup, before the SysAdmin seeder runs (registered first). Never
/// registered under the build-time OpenAPI generator (AD-4).
/// </summary>
public sealed partial class DatabaseMigrator(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    IHostEnvironment environment,
    ILogger<DatabaseMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ShouldMigrate(environment))
        {
            LogSkipped(logger);
            return;
        }

        // No data scope: migrations need no session context.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
        await db.Database.MigrateAsync(cancellationToken);
        LogMigrated(logger, pending);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information, Message = "Database migrations applied ({PendingCount} pending before start)")]
    private static partial void LogMigrated(ILogger logger, int pendingCount);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "Startup migrations disabled (EventHub:Database:MigrateOnStartup is false, or unset outside Development/Testing)")]
    private static partial void LogSkipped(ILogger logger);
}
