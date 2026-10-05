using System.Net;
using EventHub.Api.IntegrationTests.Fixtures;
using static EventHub.Api.IntegrationTests.Fixtures.SecurityHeaderAssertions;

namespace EventHub.Api.IntegrationTests.Features.OpenApi;

/// <summary>
/// AD-4: Scalar and the runtime document are Development-only. AD-22: in Development they get no CSP (the UI needs
/// scripts and styles) but keep the other headers; in Production they are 404s with the CSP.
/// </summary>
public sealed class ScalarTests
{
    [Theory]
    [InlineData("/scalar")]
    [InlineData("/openapi/v1.json")]
    public async Task DevTool_WhenProduction_Returns404WithCsp(string path)
    {
        await using var factory = new EventHubApiFactory(connectionString: null, environment: "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertBaseHeaders(response, expectCsp: true);
    }

    [Fact]
    public async Task Scalar_WhenDevelopment_IsServedWithoutCspButWithNosniff()
    {
        await using var factory = new EventHubApiFactory(connectionString: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertBaseHeaders(response, expectCsp: false);
    }

    [Fact]
    public async Task RuntimeDocument_WhenDevelopment_IsOpenApi30WithoutCspButWithNosniff()
    {
        await using var factory = new EventHubApiFactory(connectionString: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\": \"3.0.", json, StringComparison.Ordinal);
        AssertBaseHeaders(response, expectCsp: false);
    }

    /// <summary>The exemption is by path segment: look-alike paths keep the CSP.</summary>
    [Theory]
    [InlineData("/scalarx")]
    [InlineData("/openapix")]
    public async Task LookAlikePath_WhenDevelopment_KeepsCsp(string path)
    {
        await using var factory = new EventHubApiFactory(connectionString: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        AssertBaseHeaders(response, expectCsp: true);
    }
}
