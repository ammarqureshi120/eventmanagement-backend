using System.Net;
using EventHub.Api.IntegrationTests.Fixtures;

namespace EventHub.Api.IntegrationTests.Features.OpenApi;

/// <summary>AD-4: Scalar and the runtime document are Development-only.</summary>
public sealed class ScalarTests
{
    [Fact]
    public async Task Scalar_WhenProduction_Returns404()
    {
        await using var factory = new EventHubApiFactory(connectionString: null, environment: "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scalar_WhenDevelopment_IsServed()
    {
        await using var factory = new EventHubApiFactory(connectionString: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RuntimeDocument_WhenDevelopment_IsOpenApi30()
    {
        await using var factory = new EventHubApiFactory(connectionString: null);
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Contains("\"openapi\": \"3.0.", json, StringComparison.Ordinal);
    }
}
