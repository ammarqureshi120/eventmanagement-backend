#if EVENTHUB_SEED_TOOLS
using System.Net;
using System.Text.Json;
using EventHub.Api.Hosting;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Application.Common.Ports;
using EventHub.Infrastructure.Identity;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>
/// FR2 / AD-16 / AD-14 (Story 1.5): a deactivated user (through the Arrange hook; no endpoint exists) is rejected on
/// the next request with 401 <c>account_disabled</c> in every session, and sign-in reveals the disabled state only
/// after a correct password.
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class DeactivationTests(LocalSqlFixture sql)
{
    private static readonly Uri Origin = new("http://localhost");

    [Fact]
    public async Task NextRequest_WhenDeactivatedWithTwoOpenSessions_Returns401AccountDisabledInEachAndClearsTheCookie()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("deactivated");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var first = factory.CreateApiClient(out var firstBrowser);
        using var second = factory.CreateApiClient();
        await SignInAsync(first, email);
        await SignInAsync(second, email);
        var firstCookie = firstBrowser.Cookie(Origin, AuthSetup.CookieName)!;
        var userId = await UserIdAsync(sql, email);
        var stampBefore = await SecurityStampAsync(sql, userId);

        await DeactivateAsync(factory, sql, email);

        foreach (var client in new[] { first, second })
        {
            var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
            await AssertAccountDisabled(response);
            var cleared = SessionSetCookie(response);
            Assert.NotNull(cleared);
            Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
        }

        // A replayed cookie stays rejected, and the stamp was rotated after the commit.
        await AssertAccountDisabled(await GetWithCookieAsync(factory, "/api/me", firstCookie));
        Assert.NotEqual(stampBefore, await SecurityStampAsync(sql, userId));
        Assert.Equal("Inactive", (await CredentialStateAsync(sql, userId)).Status);
        await AssertOneDeactivatedAudit(userId);
    }

    [Fact]
    public async Task Login_WhenDeactivatedAndPasswordCorrect_Returns401AccountDisabledWithoutCookieOrSignedInAudit()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("disabledlogin");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, "Wrong-Passw0rd")).StatusCode);
        Assert.Equal(1, (await CredentialStateAsync(sql, userId)).AccessFailedCount);
        await DeactivateAsync(factory, sql, email);

        var response = await LoginAsync(client, email, Password);

        await AssertAccountDisabled(response);
        Assert.Null(SessionSetCookie(response));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.signedIn"));
        Assert.Equal(0, (await CredentialStateAsync(sql, userId)).AccessFailedCount);
    }

    [Fact]
    public async Task Login_WhenDeactivatedAndPasswordWrong_Returns401InvalidCredentialsAndCountsTheFailure()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("disabledwrong");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await DeactivateAsync(factory, sql, email);
        var userId = await UserIdAsync(sql, email);

        var response = await LoginAsync(client, email, "Wrong-Passw0rd");

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, raw);
        using var problem = JsonDocument.Parse(raw);
        Assert.Equal("invalid_credentials", problem.RootElement.GetProperty("code").GetString());
        Assert.Null(SessionSetCookie(response));
        Assert.Equal(1, (await CredentialStateAsync(sql, userId)).AccessFailedCount);
    }

    [Fact]
    public async Task Deactivate_WhenUserAlreadyInactive_IsANoOpWithoutASecondAuditEntry()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("twice");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient(); // starts the host, which seeds the user

        await DeactivateAsync(factory, sql, email);
        await DeactivateAsync(factory, sql, email);

        var userId = await UserIdAsync(sql, email);

        Assert.Equal("Inactive", (await CredentialStateAsync(sql, userId)).Status);
        await AssertOneDeactivatedAudit(userId);
    }

    /// <summary>Rotation runs after the commit: if it fails the deactivation stays committed and status alone still rejects.</summary>
    [Fact]
    public async Task Deactivate_WhenStampRotationFails_KeepsTheCommittedDeactivationAndStatusStillRejectsTheSession()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("rotatefails");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email), configureServices: services =>
        {
            services.RemoveAll<IIdentityAccount>();
            services.AddScoped<IIdentityAccount>(sp => new FailingRotation(ActivatorUtilities.CreateInstance<IdentityAccount>(sp)));
        });
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var stampBefore = await SecurityStampAsync(sql, userId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(factory, sql, email));

        Assert.Equal(stampBefore, await SecurityStampAsync(sql, userId));
        Assert.Equal("Inactive", (await CredentialStateAsync(sql, userId)).Status);
        await AssertOneDeactivatedAudit(userId);
        await AssertAccountDisabled(await client.GetAsync("/api/me", TestContext.Current.CancellationToken));
    }

    private async Task AssertOneDeactivatedAudit(Guid userId)
    {
        await using var connection = await sql.OpenAsync(DataScope.System, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT ActorType, ActorId, OrganizationId, Visibility, EntityType FROM dbo.AuditEntries " +
            "WHERE EntityId = @id AND Action = 'user.deactivated'", connection);
        command.Parameters.AddWithValue("@id", userId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), "Expected a user.deactivated entry.");
        Assert.Equal("System", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
        Assert.Equal("Platform", reader.GetString(3));
        Assert.Equal("User", reader.GetString(4));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken), "Expected exactly one user.deactivated entry.");
    }

    private static async Task AssertAccountDisabled(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{response.StatusCode}: {raw}");
        Assert.False(response.Headers.Contains("Location"));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(raw);
        Assert.Equal("account_disabled", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("This account is disabled.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://eventhub.dev/errors/account_disabled", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Matches("^[0-9a-f]{32}$", problem.RootElement.GetProperty("traceId").GetString());
    }

    private sealed class FailingRotation(IIdentityAccount inner) : IIdentityAccount
    {
        public Task<PasswordCheck> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            inner.VerifyPasswordAsync(email, password, cancellationToken);

        public Task<SessionAccount?> FindSessionAccountAsync(Guid userId, CancellationToken cancellationToken = default) =>
            inner.FindSessionAccountAsync(userId, cancellationToken);

        public Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated stamp rotation failure.");

        public Task SetPasswordForSeedAsync(Guid userId, string password, CancellationToken cancellationToken = default) =>
            inner.SetPasswordForSeedAsync(userId, password, cancellationToken);
    }
}
#endif
