using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Email;
using EventHub.Infrastructure.Identity;
using EventHub.Infrastructure.Ids;
using EventHub.Infrastructure.Persistence;
using EventHub.Infrastructure.Persistence.Interceptors;
using EventHub.Infrastructure.Persistence.Readers;
using EventHub.Infrastructure.Persistence.Scopes;
using EventHub.Infrastructure.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventHub.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Connection string name; the AppHost <c>AddConnectionString("eventhub")</c> injects <c>ConnectionStrings__eventhub</c>.</summary>
    public const string DatabaseConnectionName = "eventhub";

    /// <summary>
    /// Health-check tag for readiness. Must equal <c>EventHub.ServiceDefaults.Extensions.ReadyTag</c>
    /// (AD-1 keeps Infrastructure free of a ServiceDefaults reference; a test pins the two together).
    /// </summary>
    public const string ReadyTag = "ready";

    /// <param name="isDocumentGeneration">
    /// True under the build-time OpenAPI generator, which boots without a database (AD-4): no migrations, no seeding.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, bool isDocumentGeneration = false)
    {
        var connectionString = configuration.GetConnectionString(DatabaseConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString) && !isDocumentGeneration)
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{DatabaseConnectionName}' is not configured. " +
                $"Set ConnectionStrings__{DatabaseConnectionName} (the Aspire AppHost does this) or use user-secrets.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, SequentialGuidGenerator>();

        // AD-7: data scopes. The request scope follows ITenantContext; DbScopeFactory pins child scopes.
        services.AddScoped<ScopeAccessor>();
        services.AddSingleton<DbScopeFactory>();
        services.AddScoped<TimestampsInterceptor>();
        services.AddScoped<RlsSessionContextInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            // AD-22: these events log the provider exception, whose message can echo row values (an email in a
            // duplicate-key error). The exception handler logs type, SQL error number and traceId instead.
            options.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.SaveChangesFailed, RelationalEventId.CommandError));
            options.AddInterceptors(
                sp.GetRequiredService<RlsSessionContextInterceptor>(),
                sp.GetRequiredService<TimestampsInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // AD-26: Identity for credentials only, over the domain User row. No roles, no claims tables.
        // Any valid email is a user name (apostrophes, non-ASCII): no character allowlist.
        services.AddIdentityCore<User>(options => options.User.AllowedUserNameCharacters = string.Empty)
            .AddUserStore<UserStore>();
        services.AddScoped<IIdentityAccount, IdentityAccount>();

        // AD-20: read ports project through the request context, so the request's RLS scope applies.
        services.AddScoped<IUserProfileReader, UserProfileReader>();

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName));
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));

        if (!isDocumentGeneration)
        {
            // Order matters: hosted services start in registration order, so the schema exists before seeding.
            services.AddHostedService<DatabaseMigrator>();
            services.AddHostedService<SystemAdministratorSeeder>();
        }

        // AD-21: one key ring for every instance and restart, stored in the global DataProtectionKeys table.
        // Registered after the migrator: Data Protection's key-ring preload hosted service then starts after
        // migrations. Keys are not encrypted at rest yet (hosting follow-up: ProtectKeysWith* via Key Vault/KMS).
        var dataProtection = services.AddDataProtection().SetApplicationName("EventHub");
        if (!isDocumentGeneration)
        {
            dataProtection.PersistKeysToDbContext<AppDbContext>();
            services.AddHostedService<DataProtectionKeyEncryptionCheck>();
        }

        // AD-21: readiness = database only; SMTP is reported on /health detail only.
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("db", tags: [ReadyTag])
            .AddCheck<SmtpHealthCheck>("smtp");

        return services;
    }
}
