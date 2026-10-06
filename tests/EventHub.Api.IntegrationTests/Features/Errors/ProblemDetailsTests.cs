using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Api.IntegrationTests.TestApi;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace EventHub.Api.IntegrationTests.Features.Errors;

/// <summary>AD-17 / NFR4 / NFR12: every non-2xx under /api is an EventHubProblem with code + traceId and no internals.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class ProblemDetailsTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Send_WhenFieldsInvalidIncludingAListItem_Returns400WithCamelCaseDotPathKeys()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = Client(factory, TestActor.SysAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/test/items",
            new { name = "", items = new[] { new { email = "ok@example.test" }, new { email = "not-an-email" } } },
            TestContext.Current.CancellationToken);

        using var problem = await AssertProblem(response, HttpStatusCode.BadRequest, "validation");
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _), errors.ToString());
        Assert.True(errors.TryGetProperty("items.1.email", out _), errors.ToString());
        Assert.False(errors.TryGetProperty("items.0.email", out _), errors.ToString());
        Assert.Equal(0, factory.Services.GetRequiredService<TestProbe>().HandlerRuns);
    }

    [Fact]
    public async Task Send_WhenActorHasNoGrant_Returns403AndRunsNeitherValidatorNorHandler()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = Client(factory, TestActor.SysAdmin);

        var response = await client.PostAsJsonAsync("/api/test/ungranted", new { name = "" }, TestContext.Current.CancellationToken);

        using var _ = await AssertProblem(response, HttpStatusCode.Forbidden, "forbidden");
        var probe = factory.Services.GetRequiredService<TestProbe>();
        Assert.Equal(0, probe.ValidatorRuns);
        Assert.Equal(0, probe.HandlerRuns);
    }

    [Fact]
    public async Task Send_WhenAnonymousWithInvalidBody_Returns403BeforeValidation()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/test/items", new { name = "" }, TestContext.Current.CancellationToken);

        using var _ = await AssertProblem(response, HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(0, factory.Services.GetRequiredService<TestProbe>().ValidatorRuns);
    }

    [Fact]
    public async Task Send_WhenHandlerThrowsNotFound_Returns404WithoutTheMessage()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = Client(factory, TestActor.SysAdmin);

        var response = await client.GetAsync($"/api/test/missing/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        using var problem = await AssertProblem(response, HttpStatusCode.NotFound, "not_found");
        Assert.DoesNotContain("does not exist", problem.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Request_WhenApiRouteUnknown_Returns404Problem()
    {
        await using var factory = new EventHubApiFactory(connectionString: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/x", TestContext.Current.CancellationToken);

        using var _ = await AssertProblem(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Request_WhenMethodNotAllowed_Returns405WithNotFoundCode()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = Client(factory, TestActor.SysAdmin);

        var response = await client.GetAsync("/api/test/crash", TestContext.Current.CancellationToken);

        using var _ = await AssertProblem(response, HttpStatusCode.MethodNotAllowed, "not_found");
    }

    [Fact]
    public async Task Send_WhenHandlerCrashesWithSecretText_Returns500FriendlyTitleOnlyAndLogsTraceId()
    {
        sql.SkipIfUnavailable();
        const string secret = "hunter2-do-not-leak";
        var sink = new InMemoryLogSink();
        await using var factory = new EventHubApiFactory(
            sql.ConnectionString, testApi: true, configureServices: s => s.AddSingleton<ILogEventSink>(sink));
        using var client = Client(factory, TestActor.SysAdmin);

        var response = await client.PostAsJsonAsync("/api/test/crash", new { secret }, TestContext.Current.CancellationToken);

        using var problem = await AssertProblem(response, HttpStatusCode.InternalServerError, "server_error");
        var body = problem.RootElement.ToString();
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.False(problem.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind != JsonValueKind.Null);
        Assert.Equal("Something went wrong on our side. Please try again.", problem.RootElement.GetProperty("title").GetString());

        var traceId = problem.RootElement.GetProperty("traceId").GetString();
        Assert.Contains(sink.Events, e =>
            e.Level == Serilog.Events.LogEventLevel.Error
            && e.Properties.TryGetValue("TraceId", out var logged)
            && logged.ToString().Trim('"') == traceId);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Send_WhenJsonMalformed_Returns400Validation(string environment)
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, environment, testApi: true);
        using var client = Client(factory, TestActor.SysAdmin);

        using var content = new StringContent("{ \"name\": ", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/test/items", content, TestContext.Current.CancellationToken);

        using var _ = await AssertProblem(response, HttpStatusCode.BadRequest, "validation");
    }

    [Theory]
    [InlineData("/api/test/crash", HttpStatusCode.InternalServerError, "server_error")]
    [InlineData("/api/test/ungranted", HttpStatusCode.Forbidden, "forbidden")]
    public async Task Send_WhenAcceptExcludesJson_StillReturnsProblemBody(string path, HttpStatusCode status, string code)
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = Client(factory, TestActor.SysAdmin);

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { secret = "x", name = "x" }) };
        request.Headers.Accept.ParseAdd("text/html");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        using var _ = await AssertProblem(response, status, code);
    }

    [Fact]
    public async Task Send_WhenUniqueIndexViolated_Returns500AndNeverLogsTheEmail()
    {
        sql.SkipIfUnavailable();
        var email = $"dup-{Guid.NewGuid():N}@example.test";
        var sink = new InMemoryLogSink();
        await using var factory = new EventHubApiFactory(
            sql.ConnectionString, testApi: true, configureServices: s => s.AddSingleton<ILogEventSink>(sink));
        using var client = Client(factory, TestActor.SysAdmin);

        var response = await client.PostAsJsonAsync("/api/test/duplicate-user", new { email }, TestContext.Current.CancellationToken);

        using var problem = await AssertProblem(response, HttpStatusCode.InternalServerError, "server_error");
        Assert.DoesNotContain(email, problem.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        var lines = sink.RenderAll();
        Assert.Contains(lines, line => line.Contains("SQL error 2601", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    private static HttpClient Client(EventHubApiFactory factory, string actor)
    {
        // Unsafe verbs carry a valid antiforgery token (AD-16 group filter), as the SPA sends them.
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add(TestActor.Header, actor);
        return client;
    }

    private static async Task<JsonDocument> AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(status == response.StatusCode, $"Expected {status}, got {response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.Equal((int)status, root.GetProperty("status").GetInt32());
        Assert.Equal($"https://eventhub.dev/errors/{code}", root.GetProperty("type").GetString());
        Assert.Matches("^[0-9a-f]{32}$", root.GetProperty("traceId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(root.TryGetProperty("exception", out _));
        return document;
    }
}
