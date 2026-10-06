#if EVENTHUB_SEED_TOOLS
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventHub.Api.Hosting;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Api.IntegrationTests.TestApi;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>FR1 / AD-16 / AD-17 / AD-14: <c>POST /api/auth/login</c>.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class LoginTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Login_WhenCredentialsValid_Returns200WithSessionCookieClaimsAndOneSignedInAudit()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("login");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient(out var browser);
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await LoginAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var userId = await UserIdAsync(sql, email);

        // Cookie: HttpOnly, SameSite=Lax, path /, not Secure over plain http (Secure follows the request).
        var setCookie = SessionSetCookie(response);
        Assert.NotNull(setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);

        // Claims: sub, role, iat; no org_id.
        var cookieValue = browser.Cookie(new Uri("http://localhost"), AuthSetup.CookieName)![(AuthSetup.CookieName.Length + 1)..];
        var ticket = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthSetup.Scheme).TicketDataFormat.Unprotect(cookieValue);
        Assert.NotNull(ticket);
        var claims = ticket.Principal.Claims.ToList();
        Assert.Equal(userId.ToString(), Assert.Single(claims, c => c.Type == "sub").Value);
        Assert.Equal("SystemAdministrator", Assert.Single(claims, c => c.Type == "role").Value);
        var iat = long.Parse(Assert.Single(claims, c => c.Type == "iat").Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(iat, before - 5, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5);
        Assert.DoesNotContain(claims, c => c.Type == "org_id");
        Assert.DoesNotContain(claims, c => c.Value.Contains(Password, StringComparison.Ordinal));

        // Exactly one user.signedIn, actor = that user, Platform, no Organization (SM-7).
        await using var connection = await sql.OpenAsync(DataScope.System, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT ActorType, ActorId, ActorName, OrganizationId, Visibility, EntityType FROM dbo.AuditEntries " +
            "WHERE EntityId = @id AND Action = 'user.signedIn'", connection);
        command.Parameters.AddWithValue("@id", userId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("SystemAdministrator", reader.GetString(0));
        Assert.Equal(userId, reader.GetGuid(1));
        Assert.Equal($"{FirstName} {LastName}", reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal("Platform", reader.GetString(4));
        Assert.Equal("User", reader.GetString(5));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken), "Expected exactly one sign-in entry.");

        // The session works.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Login_WhenRequestIsHttps_IssuesASecureCookie()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("secure");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient("https://localhost");

        var response = await LoginAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("secure", SessionSetCookie(response)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WhenPasswordWrongEmailUnknownOrNoPasswordSet_ReturnsIdenticalInvalidCredentialsWithoutCookieOrAudit()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("wrong");
        var passwordless = UniqueEmail("nopassword");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email, passwordlessEmail: passwordless));
        using var client = factory.CreateApiClient();

        var bodies = new List<string>();
        foreach (var (candidate, password) in new[] { (email, "Wrong-Passw0rd"), (UniqueEmail("unknown"), Password), (passwordless, Password) })
        {
            var response = await LoginAsync(client, candidate, password);
            var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, raw);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Null(SessionSetCookie(response));
            Assert.False(response.Headers.Contains("Location"));
            bodies.Add(WithoutTraceId(raw));
        }

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        using var problem = JsonDocument.Parse(bodies[0]);
        Assert.Equal("invalid_credentials", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("https://eventhub.dev/errors/invalid_credentials", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, await AuditCountAsync(sql, await UserIdAsync(sql, email), "user.signedIn"));
        Assert.Equal(0, await AuditCountAsync(sql, await UserIdAsync(sql, passwordless), "user.signedIn"));
    }

    [Theory]
    [InlineData(null, "x", "email")]
    [InlineData("", "x", "email")]
    [InlineData("not-an-email", "x", "email")]
    [InlineData("a@example.test", null, "password")]
    [InlineData("a@example.test", "", "password")]
    [InlineData("   ", "x", "email")]
    public async Task Login_WhenAFieldIsEmptyOrMalformed_Returns400ValidationOnThatField(string? email, string? password, string field)
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateApiClient();

        var response = await LoginAsync(client, email, password);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, raw);
        using var problem = JsonDocument.Parse(raw);
        Assert.Equal("validation", problem.RootElement.GetProperty("code").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), errors.ToString());
        Assert.Single(errors.EnumerateObject());
        Assert.Null(SessionSetCookie(response));
    }

    [Fact]
    public async Task Login_WhenPasswordIsTooLong_Returns400OnPasswordWithItsOwnMessage()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateApiClient();

        var response = await LoginAsync(client, "a@example.test", new string('p', 1025));

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, raw);
        using var problem = JsonDocument.Parse(raw);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Single(errors.EnumerateObject());
        Assert.Equal("Use a password of at most 1024 characters.", errors.GetProperty("password")[0].GetString());
    }

    [Fact]
    public async Task Login_WhenEmailIsTooLong_Returns400OnEmail()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateApiClient();

        var response = await LoginAsync(client, new string('a', 250) + "@example.test", Password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Use an email address of at most 256 characters.", problem.RootElement.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    [Fact]
    public async Task Login_WhenEmailIsPaddedAndUpperCase_SignsIn()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("padded");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();

        var response = await LoginAsync(client, $"  {email.ToUpperInvariant()}  ", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WhenAlreadySignedIn_SignsInAgainAndReplacesTheSession()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("relogin");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);

        var again = await LoginAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.NotNull(SessionSetCookie(again));
        Assert.Equal(2, await AuditCountAsync(sql, await UserIdAsync(sql, email), "user.signedIn"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Login_WhenJsonIsMalformed_Returns400Validation()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateApiClient();
        using var content = new StringContent("{ \"email\": ", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/auth/login", content, TestContext.Current.CancellationToken);

        await AssertCode(response, HttpStatusCode.BadRequest, "validation");
        Assert.Null(SessionSetCookie(response));
    }

    [Fact]
    public async Task Login_WhenContentTypeIsNotJson_Returns415WithValidationCode()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateApiClient();
        using var content = new StringContent("email=a@example.test&password=x", Encoding.UTF8, "text/plain");

        var response = await client.PostAsync("/api/auth/login", content, TestContext.Current.CancellationToken);

        await AssertCode(response, HttpStatusCode.UnsupportedMediaType, "validation");
        Assert.Null(SessionSetCookie(response));
    }

    /// <summary>AD-7: login's unit of work (the audit insert) runs with SESSION_CONTEXT('Scope') = identity.</summary>
    [Fact]
    public async Task Login_WhenItRuns_ItsDatabaseWorkUsesTheIdentitySessionContext()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("scope");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email), testApi: true);
        using var client = factory.CreateApiClient();

        await SignInAsync(client, email);

        var probe = factory.Services.GetRequiredService<TestProbe>();
        Assert.Equal(["identity"], probe.SignInSessionScopes);
    }

    private static async Task AssertCode(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {raw}");
        using var problem = JsonDocument.Parse(raw);
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    /// <summary>NFR4: the three failure kinds take comparable time (one PBKDF2 verification each); generous bound.</summary>
    [Fact]
    [Trait("Category", "Timing")]
    public async Task Login_WhenFailingForEachReason_TakesComparableTime()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("timing");
        var passwordless = UniqueEmail("timingnopw");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email, passwordlessEmail: passwordless));
        using var client = factory.CreateApiClient();
        var cases = new[] { (email, "Wrong-Passw0rd"), (UniqueEmail("timingunknown"), Password), (passwordless, Password) };

        foreach (var (candidate, password) in cases)
        {
            await LoginAsync(client, candidate, password); // warm-up
        }

        const int tries = 9;
        var samples = cases.Select(_ => new List<double>()).ToArray();
        for (var round = 0; round < tries; round++)
        {
            for (var i = 0; i < cases.Length; i++)
            {
                var started = Stopwatch.GetTimestamp();
                var response = await LoginAsync(client, cases[i].Item1, cases[i].Item2);
                samples[i].Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }

        var medians = samples.Select(Median).ToArray();
        var ratio = medians.Max() / medians.Min();
        Assert.True(ratio < 3.0, $"Median ms wrong/unknown/no-password = {string.Join(" / ", medians.Select(m => m.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))} (ratio {ratio:0.00}).");
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    /// <summary>The raw body with only the traceId value blanked, so the comparison is byte-for-byte otherwise.</summary>
    private static string WithoutTraceId(string raw)
    {
        var traceId = JsonNode.Parse(raw)!["traceId"]!.GetValue<string>();
        Assert.Matches("^[0-9a-f]{32}$", traceId);
        return raw.Replace(traceId, "<traceId>", StringComparison.Ordinal);
    }
}
#endif
