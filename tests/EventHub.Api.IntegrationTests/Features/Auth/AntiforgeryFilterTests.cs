#if EVENTHUB_SEED_TOOLS
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Api.IntegrationTests.TestApi;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>
/// AD-16 / NFR5: the <c>/api</c> group filter rejects any unsafe verb (login included) without a valid
/// <c>X-XSRF-TOKEN</c> with the Story 1.3 400 <c>validation</c> problem, before any behavior or handler runs.
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class AntiforgeryFilterTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Login_WhenNoXsrfToken_Returns400ValidationAndRunsNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("noxsrf");
        var sink = new InMemoryLogSink();
        await using var factory = new EventHubApiFactory(
            sql.ConnectionString, settings: Seed(email), configureServices: s => s.AddSingleton<ILogEventSink>(sink));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, TestContext.Current.CancellationToken);

        await AssertValidationProblem(response);
        Assert.Null(SessionSetCookie(response));
        Assert.Equal(0, await AuditCountAsync(sql, await UserIdAsync(sql, email), "user.signedIn"));
        Assert.DoesNotContain(sink.RenderAll(), line => line.Contains("LoginCommand", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_WhenXsrfHeaderDoesNotMatchTheCookie_Returns400Validation()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("badxsrf");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = Password }),
        };
        request.Headers.Add(ApiClient.XsrfHeader, "not-the-token");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await AssertValidationProblem(response);
        Assert.Null(SessionSetCookie(response));
    }

    [Fact]
    public async Task Logout_WhenNoXsrfToken_Returns400AndTheSessionStaysValid()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("logoutnoxsrf");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient(out var browser);
        await SignInAsync(client, email);
        var session = browser.Cookie(new Uri("http://localhost"), EventHub.Api.Hosting.AuthSetup.CookieName)!;

        using var plain = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", session);
        var response = await plain.SendAsync(request, TestContext.Current.CancellationToken);

        await AssertValidationProblem(response);
        Assert.Equal(0, await AuditCountAsync(sql, await UserIdAsync(sql, email), "user.signedOut"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>Tokens are bound to the signed-in user, which is why the SPA refreshes its token after login.</summary>
    [Fact]
    public async Task Logout_WhenTokenWasIssuedBeforeSignIn_Returns400()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("staletoken");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient(out var browser);
        var origin = new Uri("http://localhost");
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/auth/antiforgery", TestContext.Current.CancellationToken)).StatusCode);
        var anonymousToken = Uri.UnescapeDataString(browser.Cookie(origin, "XSRF-TOKEN")!["XSRF-TOKEN=".Length..]);
        await SignInAsync(client, email);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add(ApiClient.XsrfHeader, anonymousToken);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await AssertValidationProblem(response);
    }

    [Fact]
    public async Task AnyApiCommand_WhenNoXsrfToken_Returns400BeforeAuthorizationValidationOrHandler()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestActor.Header, TestActor.SysAdmin);

        var response = await client.PostAsJsonAsync("/api/test/items", new { name = "" }, TestContext.Current.CancellationToken);

        await AssertValidationProblem(response);
        var probe = factory.Services.GetRequiredService<TestProbe>();
        Assert.Equal(0, probe.ValidatorRuns);
        Assert.Equal(0, probe.HandlerRuns);
    }

    [Fact]
    public async Task SafeVerbs_WhenNoXsrfToken_PassThrough()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("safe");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/auth/antiforgery", TestContext.Current.CancellationToken)).StatusCode);
    }

    private static async Task AssertValidationProblem(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(raw);
        var root = problem.RootElement;
        Assert.Equal("validation", root.GetProperty("code").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("https://eventhub.dev/errors/validation", root.GetProperty("type").GetString());
        Assert.Equal("Some fields need a look.", root.GetProperty("title").GetString());
        Assert.Matches("^[0-9a-f]{32}$", root.GetProperty("traceId").GetString());
    }
}
#endif
