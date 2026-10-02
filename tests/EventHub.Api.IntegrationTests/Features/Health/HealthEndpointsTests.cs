using System.Net;
using System.Text.Json;
using EventHub.Api.IntegrationTests.Fixtures;

namespace EventHub.Api.IntegrationTests.Features.Health;

[Collection(LocalSqlCollection.Name)]
public sealed class HealthEndpointsTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Ready_WhenDatabaseReachable_Returns200()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Live_WhenProcessUp_Returns200()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Detail_WhenSmtpUnreachable_Returns503WithOnlySmtpUnhealthy()
    {
        sql.SkipIfUnavailable();
        // The test factory clears EventHub:Smtp:Host, so the SMTP check is Unhealthy ("not configured").
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var entries = body.RootElement.GetProperty("entries");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Healthy", entries.GetProperty("db").GetProperty("status").GetString());
        Assert.Equal("Unhealthy", entries.GetProperty("smtp").GetProperty("status").GetString());
    }
}
