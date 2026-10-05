using System.Net;
using System.Net.Http.Json;
using EventHub.Api.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace EventHub.Api.IntegrationTests.Features.Logging;

/// <summary>AD-22 / NFR4: a request carrying a cookie, a password and a token leaves none of them in the logs.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class SecretsNotLoggedTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Request_WithCookiePasswordAndToken_NeverWritesThemToAnySink()
    {
        sql.SkipIfUnavailable();
        const string cookieSecret = "cookie-secret-4f1c";
        const string passwordSecret = "P@ssword-secret-9b2e";
        const string tokenSecret = "token-secret-77d0";
        var sink = new InMemoryLogSink();
        await using var factory = new EventHubApiFactory(
            sql.ConnectionString, testApi: true, configureServices: s => s.AddSingleton<ILogEventSink>(sink));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestActor.Header, TestActor.SysAdmin);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/test/echo")
        {
            Content = JsonContent.Create(new { password = passwordSecret, resetToken = tokenSecret }),
        };
        request.Headers.Add("Cookie", $".EventHub.Session={cookieSecret}");
        request.Headers.Add("X-XSRF-TOKEN", tokenSecret);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = sink.RenderAll();
        Assert.NotEmpty(lines);
        Assert.Contains(lines, line => line.Contains("EchoCommand", StringComparison.Ordinal)); // the pipeline did log
        foreach (var secret in new[] { cookieSecret, passwordSecret, tokenSecret })
        {
            Assert.DoesNotContain(lines, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }
}
