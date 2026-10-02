using EventHub.Application.Common.Ports;
using EventHub.Infrastructure.Email;
using EventHub.Infrastructure.Persistence;
using EventHub.Infrastructure.Persistence.Interceptors;
using EventHub.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
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
    /// True under the build-time OpenAPI generator, which boots without a database (AD-4).
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
        services.AddScoped<TimestampsInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(sp.GetRequiredService<TimestampsInterceptor>());
        });

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName));

        // AD-21: readiness = database only; SMTP is reported on /health detail only.
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("db", tags: [ReadyTag])
            .AddCheck<SmtpHealthCheck>("smtp");

        return services;
    }
}
