using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EventHub.Api.IntegrationTests.Fixtures;

namespace EventHub.Api.IntegrationTests.Features.Health;

/// <summary>DB-free health behavior: liveness never checks dependencies; readiness fails closed.</summary>
public sealed class HealthEndpointsWithoutDatabaseTests
{
    private const string UnreachableDatabase = EventHubApiFactory.UnreachableDatabase;

    [Fact]
    public async Task Live_WhenDatabaseDown_StillReturns200()
    {
        await using var factory = new EventHubApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WhenDatabaseDown_Returns503()
    {
        await using var factory = new EventHubApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Detail_WhenSmtpPortClosed_ReportsSmtpUnhealthyWithinProbeTimeoutAndNoExceptionText()
    {
        const int probeTimeoutSeconds = 2;
        await using var factory = new EventHubApiFactory(UnreachableDatabase, settings: new Dictionary<string, string>
        {
            ["EventHub:Smtp:Host"] = "127.0.0.1",
            ["EventHub:Smtp:Port"] = ClosedPort().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["EventHub:Smtp:ProbeTimeoutSeconds"] = probeTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        using var client = factory.CreateClient();
        await client.GetAsync("/health/live", TestContext.Current.CancellationToken); // warm up the host

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        stopwatch.Stop();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var smtp = body.RootElement.GetProperty("entries").GetProperty("smtp");
        var description = smtp.GetProperty("description").GetString() ?? string.Empty;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", smtp.GetProperty("status").GetString());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(probeTimeoutSeconds + 3), $"took {stopwatch.Elapsed}");
        Assert.DoesNotContain("Exception", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refused", description, StringComparison.OrdinalIgnoreCase);
    }

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
