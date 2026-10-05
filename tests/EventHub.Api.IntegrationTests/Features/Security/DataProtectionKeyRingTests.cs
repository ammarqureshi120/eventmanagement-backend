using System.Net;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;

namespace EventHub.Api.IntegrationTests.Features.Security;

/// <summary>
/// AD-21: the Data Protection key ring lives in the global <c>DataProtectionKeys</c> table, so antiforgery tokens
/// (and, from 1.4, session cookies) survive an API restart and work across instances.
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class DataProtectionKeyRingTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Token_WhenIssuedByAHostThatIsThenDisposed_IsAcceptedByANewHostOnTheSameDatabase()
    {
        sql.SkipIfUnavailable();
        IssuedToken token;
        await using (var hostA = new EventHubApiFactory(sql.ConnectionString, testApi: true))
        {
            token = await IssueAsync(hostA);
        }

        await using var hostB = new EventHubApiFactory(sql.ConnectionString, testApi: true);

        Assert.Equal(HttpStatusCode.NoContent, await ValidateAsync(hostB, token));
        Assert.Equal(HttpStatusCode.BadRequest, await ValidateAsync(hostB, token with { RequestToken = null }));

        Assert.True(await sql.ScalarAsync<int>(
            DataScope.None,
            "SELECT COUNT(*) FROM dbo.DataProtectionKeys WHERE FriendlyName IS NOT NULL AND Xml LIKE N'%<key %'") >= 1);
        await AssertKeyRingComesFromSqlAsync(hostB);
    }

    [Fact]
    public async Task Token_WhenIssuedByOneLiveInstance_IsAcceptedByAnotherLiveInstance()
    {
        sql.SkipIfUnavailable();
        await using var hostA = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var _ = hostA.CreateClient(); // A boots first, so B sees any key A had to create
        await using var hostB = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var __ = hostB.CreateClient();

        var token = await IssueAsync(hostA);

        Assert.Equal(HttpStatusCode.NoContent, await ValidateAsync(hostB, token));
        Assert.Equal(HttpStatusCode.NoContent, await ValidateAsync(hostA, token));
        await AssertKeyRingComesFromSqlAsync(hostA);
        await AssertKeyRingComesFromSqlAsync(hostB);
    }

    /// <summary>
    /// Without SQL persistence a dev machine silently falls back to the user-profile key folder, which would also
    /// make the token checks pass; so every key the host knows must be a row in <c>DataProtectionKeys</c>.
    /// </summary>
    private async Task AssertKeyRingComesFromSqlAsync(EventHubApiFactory host)
    {
        var keys = host.Services.GetRequiredService<IKeyManager>().GetAllKeys();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.Equal(1, await sql.ScalarAsync<int>(
                DataScope.None,
                "SELECT COUNT(*) FROM dbo.DataProtectionKeys WHERE FriendlyName = @name",
                ("@name", $"key-{key.KeyId:D}")));
        }
    }

    /// <summary>Test hosts share a content root, so token survival alone cannot prove the fixed app name.</summary>
    [Fact]
    public async Task Options_WhenSqlBacked_UseTheEventHubApplicationDiscriminator()
    {
        sql.SkipIfUnavailable();
        await using var host = new EventHubApiFactory(sql.ConnectionString);

        var options = host.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        Assert.Equal("EventHub", options.ApplicationDiscriminator);
    }

    [Theory]
    [InlineData("Production", false, true)]
    [InlineData("Staging", false, true)]
    [InlineData("Production", true, false)]
    [InlineData("Development", false, false)]
    [InlineData("Testing", false, false)]
    public async Task Start_WhenKeysAreNotEncryptedOutsideDevelopment_LogsAnErrorButStillStarts(
        string environment, bool withEncryptor, bool expectError)
    {
        var sink = new InMemoryLogSink();
        await using var host = new EventHubApiFactory(connectionString: null, environment, configureServices: services =>
        {
            services.AddSingleton<ILogEventSink>(sink);
            if (withEncryptor)
            {
                services.Configure<KeyManagementOptions>(options => options.XmlEncryptor = new NullXmlEncryptor());
            }
        });
        using var client = host.CreateClient();

        var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(expectError, sink.Events.Any(e =>
            e.Level == LogEventLevel.Error
            && e.MessageTemplate.Text.Contains("Data Protection keys are stored unencrypted", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Token_WhenHostHasNoDatabase_StillValidatesWithEphemeralKeys()
    {
        await using var host = new EventHubApiFactory(connectionString: null, testApi: true);

        var token = await IssueAsync(host);

        Assert.Equal(HttpStatusCode.NoContent, await ValidateAsync(host, token));
    }

    private static async Task<IssuedToken> IssueAsync(EventHubApiFactory host)
    {
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.GetAsync("/api/auth/antiforgery", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var cookies = setCookies.Select(c => c.Split(';', 2)[0]).ToList();
        var requestToken = cookies.Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))["XSRF-TOKEN=".Length..];
        return new IssuedToken(string.Join("; ", cookies), Uri.UnescapeDataString(requestToken));
    }

    private static async Task<HttpStatusCode> ValidateAsync(EventHubApiFactory host, IssuedToken token)
    {
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/test/antiforgery");
        request.Headers.Add("Cookie", token.CookieHeader);
        if (token.RequestToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", token.RequestToken);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private sealed record IssuedToken(string CookieHeader, string? RequestToken);
}
