using EventHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Persistence;

/// <summary>
/// EF Core context shell. No tables yet: entities, RLS and the foundation migration arrive in Story 1.3.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // AD-12: every DateTime is stored as UTC datetime2 and materialized as DateTimeKind.Utc.
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>()
            .HaveColumnType("datetime2");
        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableUtcDateTimeConverter>()
            .HaveColumnType("datetime2");
    }

    // Story 1.3 adds OnModelCreating with ApplyConfigurationsFromAssembly once the first
    // Persistence/Configurations/<Feature>/ entity configuration exists (AD-30).
}
