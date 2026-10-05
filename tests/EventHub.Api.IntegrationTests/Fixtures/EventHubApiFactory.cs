using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real API in-process against the test database (or an unreachable one, for DB-free checks).
/// <list type="bullet">
/// <item>No database: startup migrations are switched off, and Data Protection uses ephemeral keys with an
/// in-memory key store (test-only), so nothing tries SQL for keys. With a database, keys persist to it.</item>
/// <item>Seed settings come only from <paramref name="settings"/>; otherwise the seed list is blanked so dev
/// user-secrets never seed the test database.</item>
/// <item><paramref name="testApi"/>: swaps in the test-only Mediator, grants, messages and <c>/api/test/*</c>
/// endpoints (<see cref="TestPipeline"/>); the shipped app and <c>openapi.json</c> never see them.</item>
/// </list>
/// </summary>
public sealed class EventHubApiFactory(
    string? connectionString,
    string environment = "Development",
    IReadOnlyDictionary<string, string>? settings = null,
    bool testApi = false,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Valid but unreachable: the API boots (fail-fast needs a value) and DB checks report Unhealthy.</summary>
    public const string UnreachableDatabase =
        "Server=127.0.0.1,1;Database=eventhub;User Id=sa;Password=x;Encrypt=False;Connect Timeout=1";

    public FakeClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:eventhub", connectionString ?? UnreachableDatabase);
        builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        // Keep dev user-secrets / env SMTP hosts out of tests unless a test sets one.
        builder.UseSetting("EventHub:Smtp:Host", string.Empty);

        var all = settings ?? new Dictionary<string, string>();
        var isDatabaseFree = connectionString is null || connectionString == UnreachableDatabase;
        if (isDatabaseFree)
        {
            builder.UseSetting("EventHub:Database:MigrateOnStartup", "false");
        }

        if (!all.Keys.Any(key => key.StartsWith("EventHub:Seed:", StringComparison.OrdinalIgnoreCase)))
        {
            builder.UseSetting("EventHub:Seed:SystemAdministrators:0", string.Empty);
        }

        foreach (var (key, value) in all)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            if (isDatabaseFree)
            {
                // Test-only: ephemeral keys, and the key-ring preload reads an in-memory store instead of SQL.
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());
            }

            if (testApi)
            {
                TestPipeline.AddTo(services, Clock);
            }

            configureServices?.Invoke(services);
        });
    }
}
