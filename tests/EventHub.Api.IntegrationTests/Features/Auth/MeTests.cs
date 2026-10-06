#if EVENTHUB_SEED_TOOLS
using System.Net;
using System.Text.Json;
using EventHub.Api.Hosting;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>FR5 / AD-9 / AD-16 / AD-21: <c>GET /api/me</c>, its 401, and sessions surviving a restart.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class MeTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task GetMe_WhenSignedIn_ReturnsIdentityRoleNullOrganizationVersionAndNavAreas()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("me");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);

        var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
        using var me = JsonDocument.Parse(raw);
        var root = me.RootElement;
        Assert.Equal((await UserIdAsync(sql, email)).ToString(), root.GetProperty("id").GetString());
        Assert.Equal(email, root.GetProperty("email").GetString());
        Assert.Equal(FirstName, root.GetProperty("firstName").GetString());
        Assert.Equal(LastName, root.GetProperty("lastName").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("phone").ValueKind);
        Assert.Equal("systemAdministrator", root.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("organization").ValueKind);
        Assert.True(root.GetProperty("version").GetInt32() >= 1);
        Assert.Equal(["platform", "account"], root.GetProperty("navAreas").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(9, root.EnumerateObject().Count());
    }

    [Fact]
    public async Task GetMe_WhenNoCookie_Returns401SessionExpiredNotARedirect()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        await AssertSessionExpired(response);
    }

    [Fact]
    public async Task GetMe_WhenCookieIsGarbage_Returns401SessionExpired()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);

        var response = await GetWithCookieAsync(factory, "/api/me", $"{AuthSetup.CookieName}=not-a-real-ticket");

        await AssertSessionExpired(response);
    }

    [Fact]
    public async Task GetMe_WhenCookieWasIssuedByAHostThatIsThenDisposed_IsAcceptedByANewHostOnTheSameDatabase()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("restart");
        string cookie;
        await using (var hostA = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email)))
        {
            using var client = hostA.CreateApiClient(out var browser);
            await SignInAsync(client, email);
            cookie = browser.Cookie(new Uri("http://localhost"), AuthSetup.CookieName)!;
        }

        await using var hostB = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));

        var response = await GetWithCookieAsync(hostB, "/api/me", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The validator rebuilds the role from the row on every request (AD-16).</summary>
    [Fact]
    public async Task GetMe_WhenRoleChangedAfterSignIn_UsesTheRowRole()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("rolechange");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);

        await sql.ExecuteAsync(
            DataScope.Identity,
            "UPDATE dbo.Users SET Role = 'OrgAdministrator', OrganizationId = NEWID() WHERE Id = @id",
            ("@id", await UserIdAsync(sql, email)));

        // An Org Administrator holds no Me.Get cell in Story 1.4 (and has no org claim): 403 forbidden.
        var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("\"forbidden\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMe_WhenUserRowRemoved_Returns401SessionExpiredAndDeletesTheCookie()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("removed");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient(out var browser);
        await SignInAsync(client, email);
        var cookie = browser.Cookie(new Uri("http://localhost"), AuthSetup.CookieName)!;

        await sql.ExecuteAsync(DataScope.Identity, "DELETE FROM dbo.Users WHERE Id = @id", ("@id", await UserIdAsync(sql, email)));

        var response = await GetWithCookieAsync(factory, "/api/me", cookie);
        await AssertSessionExpired(response);
        var cleared = Assert.Single(response.Headers.GetValues("Set-Cookie"), v => v.StartsWith(AuthSetup.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A role claim that does not match the row (tampered or unknown value) never wins: the row's role does.</summary>
    [Fact]
    public async Task GetMe_WhenTheCookieCarriesAnUnknownRoleClaim_UsesTheRowRole()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("tampered");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient(out var browser);
        await SignInAsync(client, email);
        var format = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AuthSetup.Scheme).TicketDataFormat;
        var original = format.Unprotect(browser.Cookie(new Uri("http://localhost"), AuthSetup.CookieName)![(AuthSetup.CookieName.Length + 1)..])!;
        var claims = original.Principal.Claims
            .Select(c => c.Type == SessionClaims.Role ? new System.Security.Claims.Claim(c.Type, "Superuser") : c);
        var forged = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, AuthSetup.Scheme, SessionClaims.Name, SessionClaims.Role));
        var cookie = $"{AuthSetup.CookieName}={format.Protect(new AuthenticationTicket(forged, original.Properties, AuthSetup.Scheme))}";

        var response = await GetWithCookieAsync(factory, "/api/me", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("systemAdministrator", me.RootElement.GetProperty("role").GetString());
    }

    internal static async Task AssertSessionExpired(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{response.StatusCode}: {raw}");
        Assert.False(response.Headers.Contains("Location"));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(raw);
        Assert.Equal("session_expired", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Matches("^[0-9a-f]{32}$", problem.RootElement.GetProperty("traceId").GetString());
    }
}
#endif
