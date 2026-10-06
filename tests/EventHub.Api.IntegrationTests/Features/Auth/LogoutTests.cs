#if EVENTHUB_SEED_TOOLS
using System.Net;
using EventHub.Api.Hosting;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Application.Common.Ports;
using EventHub.Infrastructure.Identity;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>FR1 / AD-16 / AD-14: <c>POST /api/auth/logout</c> ends every session of the user.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class LogoutTests(LocalSqlFixture sql)
{
    private static readonly Uri Origin = new("http://localhost");

    [Fact]
    public async Task Logout_WhenSignedIn_Returns204ClearsCookieAuditsOnceRotatesStampAndRejectsAReplayedCookie()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("logout");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient(out var browser);
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var oldCookie = browser.Cookie(Origin, AuthSetup.CookieName)!;
        var stampBefore = await SecurityStampAsync(sql, userId);

        var response = await client.PostAsync("/api/auth/logout", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertCookieCleared(response);
        Assert.Null(browser.Cookie(Origin, AuthSetup.CookieName));
        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.signedOut"));
        Assert.Equal("SystemAdministrator", await sql.ScalarAsync<string>(
            DataScope.System, "SELECT ActorType FROM dbo.AuditEntries WHERE EntityId = @id AND Action = 'user.signedOut'", ("@id", userId)));
        Assert.Equal("Platform", await sql.ScalarAsync<string>(
            DataScope.System, "SELECT Visibility FROM dbo.AuditEntries WHERE EntityId = @id AND Action = 'user.signedOut' AND OrganizationId IS NULL AND ActorId = @id", ("@id", userId)));
        Assert.NotEqual(stampBefore, await SecurityStampAsync(sql, userId));

        // A copy of the old cookie (this or any other device) is rejected.
        await MeTests.AssertSessionExpired(await GetWithCookieAsync(factory, "/api/me", oldCookie));
        await MeTests.AssertSessionExpired(await client.GetAsync("/api/me", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Logout_WhenSignedInOnTwoDevices_EndsBothSessions()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("twodevices");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var deviceA = factory.CreateApiClient();
        using var deviceB = factory.CreateApiClient();
        await SignInAsync(deviceA, email);
        await SignInAsync(deviceB, email);

        Assert.Equal(HttpStatusCode.NoContent, (await deviceA.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken)).StatusCode);

        await MeTests.AssertSessionExpired(await deviceB.GetAsync("/api/me", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Logout_WhenNotSignedIn_Returns401SessionExpired()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateApiClient();

        var response = await client.PostAsync("/api/auth/logout", content: null, TestContext.Current.CancellationToken);

        await MeTests.AssertSessionExpired(response);
    }

    /// <summary>Rotation runs after the commit; if it fails the request is a 500 and the cookie is still cleared.</summary>
    [Fact]
    public async Task Logout_WhenStampRotationFails_Returns500ClearsTheCookieAndKeepsTheCommittedAudit()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("rotatefail");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email), configureServices: services =>
        {
            services.RemoveAll<IIdentityAccount>();
            services.AddScoped<IIdentityAccount>(sp => new FailingRotation(new IdentityAccount(sp.GetRequiredService<DbScopeFactory>())));
        });
        using var client = factory.CreateApiClient(out var browser);
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var oldCookie = browser.Cookie(Origin, AuthSetup.CookieName)!;
        var stampBefore = await SecurityStampAsync(sql, userId);

        var response = await client.PostAsync("/api/auth/logout", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("server_error", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        AssertCookieCleared(response);
        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.signedOut"));
        Assert.Equal(stampBefore, await SecurityStampAsync(sql, userId));

        // Accepted and documented: the old cookie stays valid until the next successful logout.
        Assert.Equal(HttpStatusCode.OK, (await GetWithCookieAsync(factory, "/api/me", oldCookie)).StatusCode);
    }

    /// <summary>A client abort after the sign-out committed must not skip the stamp rotation.</summary>
    [Fact]
    public async Task Logout_WhenClientAbortsAfterTheCommit_StillRotatesTheStampSoTheOldCookieIsRejected()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("abort");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email), configureServices: services =>
        {
            services.RemoveAll<IIdentityAccount>();
            services.AddScoped<IIdentityAccount>(sp => new AbortBeforeRotation(
                new IdentityAccount(sp.GetRequiredService<DbScopeFactory>()),
                sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()));
        });
        using var client = factory.CreateApiClient(out var browser);
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var oldCookie = browser.Cookie(Origin, AuthSetup.CookieName)!;
        var stampBefore = await SecurityStampAsync(sql, userId);

        try
        {
            await client.PostAsync("/api/auth/logout", content: null, TestContext.Current.CancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // The request was aborted from the server side; the client may see that as a failure.
        }

        // The server finishes the request after the abort; wait (bounded) for the rotation.
        for (var i = 0; i < 100 && await SecurityStampAsync(sql, userId) == stampBefore; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.NotEqual(stampBefore, await SecurityStampAsync(sql, userId));
        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.signedOut"));
        await MeTests.AssertSessionExpired(await GetWithCookieAsync(factory, "/api/me", oldCookie));
    }

    private static void AssertCookieCleared(HttpResponseMessage response)
    {
        var cleared = SessionSetCookie(response);
        Assert.NotNull(cleared);
        Assert.StartsWith(AuthSetup.CookieName + "=;", cleared, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Aborts the HTTP request (cancelling RequestAborted) right before the post-commit rotation.</summary>
    private sealed class AbortBeforeRotation(IIdentityAccount inner, Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : IIdentityAccount
    {
        public Task<SessionAccount?> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            inner.VerifyPasswordAsync(email, password, cancellationToken);

        public Task<SessionAccount?> FindSessionAccountAsync(Guid userId, CancellationToken cancellationToken = default) =>
            inner.FindSessionAccountAsync(userId, cancellationToken);

        public Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            accessor.HttpContext!.Abort();
            return inner.RotateSecurityStampAsync(userId, cancellationToken);
        }

        public Task SetPasswordForSeedAsync(Guid userId, string password, CancellationToken cancellationToken = default) =>
            inner.SetPasswordForSeedAsync(userId, password, cancellationToken);
    }

    private sealed class FailingRotation(IIdentityAccount inner) : IIdentityAccount
    {
        public Task<SessionAccount?> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
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
