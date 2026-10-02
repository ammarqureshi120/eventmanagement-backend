using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>Boots the real API in-process against the test database (or an unreachable one, for DB-free checks).</summary>
public sealed class EventHubApiFactory(
    string? connectionString,
    string environment = "Development",
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Valid but unreachable: the API boots (fail-fast needs a value) and DB checks report Unhealthy.</summary>
    public const string UnreachableDatabase =
        "Server=127.0.0.1,1;Database=eventhub;User Id=sa;Password=x;Encrypt=False;Connect Timeout=1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:eventhub", connectionString ?? UnreachableDatabase);
        builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        // Keep dev user-secrets / env SMTP hosts out of tests unless a test sets one.
        builder.UseSetting("EventHub:Smtp:Host", string.Empty);

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }
    }
}
