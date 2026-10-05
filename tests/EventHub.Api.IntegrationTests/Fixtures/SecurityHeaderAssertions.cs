using Microsoft.AspNetCore.Mvc.Testing;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>AD-22 / NFR4: shared checks for the API security headers.</summary>
public static class SecurityHeaderAssertions
{
    public const string Csp = "default-src 'none'; frame-ancestors 'none'";

    public const string Hsts = "max-age=31536000";

    /// <summary>nosniff and Referrer-Policy always; the CSP when <paramref name="expectCsp"/>, else none.</summary>
    public static void AssertBaseHeaders(HttpResponseMessage response, bool expectCsp)
    {
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        if (expectCsp)
        {
            Assert.Equal(Csp, Header(response, "Content-Security-Policy"));
        }
        else
        {
            Assert.Null(Header(response, "Content-Security-Policy"));
        }
    }

    public static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;

    public static HttpClient Client(EventHubApiFactory factory, string baseAddress) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(baseAddress) });
}
