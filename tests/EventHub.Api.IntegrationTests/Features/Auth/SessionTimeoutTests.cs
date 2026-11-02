#if EVENTHUB_SEED_TOOLS
using System.Net;
using EventHub.Api.Hosting;
using EventHub.Api.IntegrationTests.Fixtures;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>
/// FR1 / AD-16 (Story 1.5): a 30-minute idle window renewed on every authenticated request, and a 12-hour absolute
/// limit from <c>iat</c>, both timed by <c>FakeClock</c> through the IClock-backed app TimeProvider.
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class SessionTimeoutTests(LocalSqlFixture sql)
{
    private static readonly Uri Origin = new("http://localhost");

    [Fact]
    public async Task Me_When30MinutesPassedSinceTheLastAuthenticatedRequest_Returns401SessionExpired()
    {
        sql.SkipIfUnavailable();
        var (factory, client, signedInAt) = await SignedInAsync("idle");
        await using (factory)
        using (client)
        {
            factory.Clock.UtcNow = signedInAt.AddMinutes(30);

            await MeTests.AssertSessionExpired(await client.GetAsync("/api/me", TestContext.Current.CancellationToken));
        }
    }

    /// <summary>Past the idle end the cookie handler drops the ticket itself; the stale cookie is still deleted.</summary>
    [Fact]
    public async Task Me_When31MinutesPassedSinceTheLastAuthenticatedRequest_Returns401SessionExpiredAndClearsTheCookie()
    {
        sql.SkipIfUnavailable();
        var (factory, client, signedInAt) = await SignedInAsync("idlepast");
        await using (factory)
        using (client)
        {
            factory.Clock.UtcNow = signedInAt.AddMinutes(31);
            var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

            await MeTests.AssertSessionExpired(response);
            var cleared = Assert.Single(response.Headers.GetValues("Set-Cookie"), v => v.StartsWith(AuthSetup.CookieName + "=", StringComparison.Ordinal));
            Assert.StartsWith(AuthSetup.CookieName + "=;", cleared, StringComparison.Ordinal);
            Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", cleared, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Renewal on every request, not only after half the window: +10 renews, so +39 is still inside.</summary>
    [Fact]
    public async Task Me_WhenARequestComesEarlyInTheWindow_StillRenewsSoARequest29MinutesLaterSucceeds()
    {
        sql.SkipIfUnavailable();
        var (factory, client, signedInAt) = await SignedInAsync("earlyrenew");
        await using (factory)
        using (client)
        {
            factory.Clock.UtcNow = signedInAt.AddMinutes(10);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);

            factory.Clock.UtcNow = signedInAt.AddMinutes(39);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
        }
    }

    [Fact]
    public async Task Me_WhenARequestComesAt29Minutes_RenewsTheCookieSoTheSessionLastsAnother30()
    {
        sql.SkipIfUnavailable();
        var (factory, client, signedInAt) = await SignedInAsync("renew");
        await using (factory)
        using (client)
        {
            factory.Clock.UtcNow = signedInAt.AddMinutes(29);
            var renewed = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            Assert.NotNull(SessionSetCookie(renewed));

            factory.Clock.UtcNow = signedInAt.AddMinutes(58);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);

            // Idle again for 30 minutes after the last request: expired.
            factory.Clock.UtcNow = signedInAt.AddMinutes(88);
            await MeTests.AssertSessionExpired(await client.GetAsync("/api/me", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Me_WhenTheRenewedCookieIsReplayedAfterTheOldWindow_IsStillAcceptedButTheOldCookieIsNot()
    {
        sql.SkipIfUnavailable();
        var (factory, client, signedInAt) = await SignedInAsync("replay");
        await using (factory)
        using (client)
        {
            var original = client.Handler.Cookie(Origin, AuthSetup.CookieName)!;
            factory.Clock.UtcNow = signedInAt.AddMinutes(20);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
            var renewed = client.Handler.Cookie(Origin, AuthSetup.CookieName)!;
            Assert.NotEqual(original, renewed);

            factory.Clock.UtcNow = signedInAt.AddMinutes(40);
            await MeTests.AssertSessionExpired(await GetWithCookieAsync(factory, "/api/me", original));
            Assert.Equal(HttpStatusCode.OK, (await GetWithCookieAsync(factory, "/api/me", renewed)).StatusCode);
        }
    }

    [Fact]
    public async Task Me_When12HoursPassedSinceSignInDespiteConstantActivity_Returns401SessionExpiredAndClearsTheCookie()
    {
        sql.SkipIfUnavailable();
        var (factory, client, signedInAt) = await SignedInAsync("absolute");
        await using (factory)
        using (client)
        {
            for (var minutes = 25; minutes < 12 * 60; minutes += 25)
            {
                factory.Clock.UtcNow = signedInAt.AddMinutes(minutes);
                var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{minutes} min: {response.StatusCode}");
            }

            factory.Clock.UtcNow = signedInAt.AddHours(12);
            var expired = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

            await MeTests.AssertSessionExpired(expired);
            var cleared = SessionSetCookie(expired);
            Assert.NotNull(cleared);
            Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<(EventHubApiFactory Factory, SessionClient Client, DateTime SignedInAt)> SignedInAsync(string name)
    {
        var email = UniqueEmail(name);
        var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email), fakeClock: true);
        var now = DateTime.UtcNow;
        factory.Clock.UtcNow = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var http = factory.CreateApiClient(out var handler);
        await SignInAsync(http, email);
        return (factory, new SessionClient(http, handler), factory.Clock.UtcNow);
    }

    /// <summary>An API client with its cookie jar.</summary>
    private sealed class SessionClient(HttpClient http, BrowserHandler handler) : IDisposable
    {
        public BrowserHandler Handler => handler;

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken) => http.GetAsync(path, cancellationToken);

        public void Dispose() => http.Dispose();
    }
}
#endif
