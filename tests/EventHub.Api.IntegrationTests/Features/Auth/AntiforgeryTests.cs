using System.Net;
using EventHub.Api.IntegrationTests.Fixtures;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>AD-16: the antiforgery endpoint needs no database.</summary>
public sealed class AntiforgeryTests
{
    [Fact]
    public async Task GetAntiforgeryToken_WhenCalled_Returns204AndJsReadableXsrfCookie()
    {
        await using var factory = new EventHubApiFactory(connectionString: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/antiforgery", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var xsrf = Assert.Single(cookies, c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(cookies, c => c.StartsWith(".EventHub.Antiforgery=", StringComparison.Ordinal)
                                      && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }
}
