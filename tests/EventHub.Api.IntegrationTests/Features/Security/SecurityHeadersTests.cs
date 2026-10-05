using System.Net;
using System.Net.Http.Json;
using EventHub.Api.IntegrationTests.Fixtures;
using static EventHub.Api.IntegrationTests.Fixtures.SecurityHeaderAssertions;

namespace EventHub.Api.IntegrationTests.Features.Security;

/// <summary>AD-22 / NFR4: API security headers on every response; HSTS only outside Development over HTTPS.</summary>
public sealed class SecurityHeadersTests
{
    [Theory]
    [InlineData("/api/auth/antiforgery", HttpStatusCode.NoContent)]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/api/x", HttpStatusCode.NotFound)]
    public async Task Response_WhenDevelopment_HasNosniffCspAndReferrerPolicyButNoHsts(string path, HttpStatusCode status)
    {
        await using var factory = new EventHubApiFactory(connectionString: null);
        using var client = Client(factory, "https://eventhub.test");

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        AssertBaseHeaders(response, expectCsp: true);
        Assert.Null(Header(response, "Strict-Transport-Security"));
    }

    [Theory]
    [InlineData("/api/auth/antiforgery", HttpStatusCode.NoContent)]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/api/x", HttpStatusCode.NotFound)]
    public async Task Response_WhenProductionOverHttps_HasAllFourHeaders(string path, HttpStatusCode status)
    {
        await using var factory = new EventHubApiFactory(connectionString: null, environment: "Production");
        using var client = Client(factory, "https://eventhub.test");

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        AssertBaseHeaders(response, expectCsp: true);
        Assert.Equal(Hsts, Header(response, "Strict-Transport-Security"));
    }

    [Theory]
    [InlineData("http://eventhub.test", null)]
    [InlineData("https://localhost", null)]
    [InlineData("https://127.0.0.1", null)]
    [InlineData("https://[::1]", null)]
    [InlineData("https://eventhub.test", "LOCALHOST")]
    public async Task Response_WhenProductionOverHttpOrExcludedHost_HasNoHsts(string baseAddress, string? hostHeader)
    {
        await using var factory = new EventHubApiFactory(connectionString: null, environment: "Production");
        using var client = Client(factory, baseAddress);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        if (hostHeader is not null)
        {
            request.Headers.Host = hostHeader; // Uri lower-cases hosts; the header keeps the case
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertBaseHeaders(response, expectCsp: true);
        Assert.Null(Header(response, "Strict-Transport-Security"));
    }
}

/// <summary>The 500 path: the exception handler's <c>Response.Clear()</c> must not drop the headers.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class SecurityHeadersOnErrorTests(LocalSqlFixture sql)
{
    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task Send_WhenHandlerCrashes_500StillHasTheHeaders(string environment, bool expectHsts)
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, environment, testApi: true);
        using var client = Client(factory, "https://eventhub.test");
        client.DefaultRequestHeaders.Add(TestActor.Header, TestActor.SysAdmin);

        var response = await client.PostAsJsonAsync("/api/test/crash", new { secret = "x" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertBaseHeaders(response, expectCsp: true);
        Assert.Equal(expectHsts ? Hsts : null, Header(response, "Strict-Transport-Security"));
    }
}
