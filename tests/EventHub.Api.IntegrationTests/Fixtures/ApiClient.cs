using System.Net;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>
/// A browser-like test client for the AD-16 rules: it keeps cookies (the session and antiforgery cookies) and, before
/// every POST/PUT/PATCH/DELETE without an explicit <c>X-XSRF-TOKEN</c>, fetches a fresh token from
/// <c>GET /api/auth/antiforgery</c> and echoes it, as the SPA does. A fresh token per request also covers the
/// "refresh after login and logout" rule (tokens are bound to the signed-in user).
/// </summary>
public static class ApiClient
{
    public const string XsrfHeader = "X-XSRF-TOKEN";

    /// <summary>A client whose unsafe requests carry a valid antiforgery token.</summary>
    public static HttpClient CreateApiClient(this EventHubApiFactory factory, string baseAddress = "http://localhost") =>
        CreateApiClient(factory, out _, baseAddress);

    /// <inheritdoc cref="CreateApiClient(EventHubApiFactory, string)"/>
    public static HttpClient CreateApiClient(this EventHubApiFactory factory, out BrowserHandler handler, string baseAddress = "http://localhost")
    {
        ArgumentNullException.ThrowIfNull(factory);
        handler = new BrowserHandler();
        return factory.CreateDefaultClient(new Uri(baseAddress), handler);
    }
}

/// <summary>Cookie jar + antiforgery echo; see <see cref="ApiClient"/>.</summary>
public sealed class BrowserHandler : DelegatingHandler
{
    public CookieContainer Cookies { get; } = new();

    /// <summary>The current <c>name=value</c> of a cookie for <paramref name="uri"/>, or null.</summary>
    public string? Cookie(Uri uri, string name) =>
        Cookies.GetCookies(uri).FirstOrDefault(cookie => cookie.Name == name) is { } found ? $"{found.Name}={found.Value}" : null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsUnsafe(request.Method) && !request.Headers.Contains(ApiClient.XsrfHeader))
        {
            using var issue = new HttpRequestMessage(HttpMethod.Get, new Uri(request.RequestUri!, "/api/auth/antiforgery"));
            using var issued = await SendWithCookiesAsync(issue, cancellationToken);
            if (issued.StatusCode != HttpStatusCode.NoContent)
            {
                throw new InvalidOperationException($"Antiforgery token request failed with {issued.StatusCode}.");
            }

            var token = Cookies.GetCookies(request.RequestUri!).Single(cookie => cookie.Name == "XSRF-TOKEN").Value;
            request.Headers.Add(ApiClient.XsrfHeader, Uri.UnescapeDataString(token));
        }

        return await SendWithCookiesAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithCookiesAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var jar = Cookies.GetCookieHeader(request.RequestUri!);
        if (jar.Length > 0)
        {
            var existing = request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : null;
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", existing is null ? jar : $"{existing}; {jar}");
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                Cookies.SetCookies(request.RequestUri!, setCookie);
            }
        }

        return response;
    }

    private static bool IsUnsafe(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch || method == HttpMethod.Delete;
}
