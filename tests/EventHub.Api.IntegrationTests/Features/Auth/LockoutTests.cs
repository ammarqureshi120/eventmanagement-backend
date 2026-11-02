#if EVENTHUB_SEED_TOOLS
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>
/// FR1 / AD-16 / NFR4 (Story 1.5): 5 consecutive failures lock the account for 15 minutes (timed by <c>IClock</c>);
/// locked, wrong-password and unknown-email attempts all get the same 401 <c>invalid_credentials</c>.
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class LockoutTests(LocalSqlFixture sql)
{
    private const string WrongPassword = "Wrong-Passw0rd";

    [Fact]
    public async Task Login_WhenFifthConsecutiveFailure_LocksFor15MinutesWithTheSameInvalidCredentials()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("lock");
        await using var factory = Factory(email);
        using var client = factory.CreateApiClient();
        var unknownBody = await FailureBodyAsync(client, UniqueEmail("unknown"), Password);
        var userId = await UserIdAsync(sql, email);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(unknownBody, await FailureBodyAsync(client, email, WrongPassword));
            Assert.Equal((attempt, (DateTime?)null), Counts(await CredentialStateAsync(sql, userId)));
        }

        Assert.Equal(unknownBody, await FailureBodyAsync(client, email, WrongPassword));

        var (count, lockoutEnd, _) = await CredentialStateAsync(sql, userId);
        Assert.Equal(factory.Clock.UtcNow.AddMinutes(15), lockoutEnd);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Login_WhenLockedAndPasswordCorrect_ReturnsIdenticalInvalidCredentialsWithoutCookieAuditOrCountChange()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("locked");
        await using var factory = Factory(email);
        using var client = factory.CreateApiClient();
        var unknownBody = await FailureBodyAsync(client, UniqueEmail("unknown"), Password);
        await LockAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await CredentialStateAsync(sql, userId);

        factory.Clock.UtcNow = factory.Clock.UtcNow.AddMinutes(14).AddSeconds(59);
        var response = await LoginAsync(client, email, Password);

        Assert.Equal(unknownBody, await BodyOf(response));
        Assert.Null(SessionSetCookie(response));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.signedIn"));
        Assert.Equal(before, await CredentialStateAsync(sql, userId));
    }

    [Fact]
    public async Task Login_WhenLockedAndAlsoDeactivated_StillReturnsInvalidCredentialsNeverAccountDisabled()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("lockeddisabled");
        await using var factory = Factory(email);
        using var client = factory.CreateApiClient();
        var unknownBody = await FailureBodyAsync(client, UniqueEmail("unknown"), Password);
        await LockAsync(client, email);
        await DeactivateAsync(factory, sql, email);

        Assert.Equal(unknownBody, await FailureBodyAsync(client, email, Password));
    }

    [Fact]
    public async Task Login_When15MinutesPassedSinceTheLock_CorrectPasswordSignsInAndResetsTheCount()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("unlock");
        await using var factory = Factory(email);
        using var client = factory.CreateApiClient();
        await LockAsync(client, email);
        var userId = await UserIdAsync(sql, email);

        factory.Clock.UtcNow = factory.Clock.UtcNow.AddMinutes(15);
        var response = await LoginAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(SessionSetCookie(response));
        Assert.Equal((0, (DateTime?)null), Counts(await CredentialStateAsync(sql, userId)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Login_WhenSuccessBeforeTheFifthFailure_ResetsTheCountToZero()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("reset");
        await using var factory = Factory(email);
        using var guesser = factory.CreateApiClient();
        using var owner = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await FailureBodyAsync(guesser, email, WrongPassword);
        }

        Assert.Equal(4, (await CredentialStateAsync(sql, userId)).AccessFailedCount);
        await SignInAsync(owner, email);
        Assert.Equal((0, (DateTime?)null), Counts(await CredentialStateAsync(sql, userId)));

        // Four more failures do not lock: the count started again from zero.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await FailureBodyAsync(guesser, email, WrongPassword);
        }

        await SignInAsync(guesser, email);
    }

    /// <summary>
    /// A concurrent update of the row between the failure-count read and its save (stale <c>Version</c>) is retried:
    /// the row is reloaded and the failure applied again, so it is still counted exactly once.
    /// </summary>
    [Fact]
    public async Task Login_WhenTheRowChangesBeforeTheFailureIsSaved_RetriesAndCountsTheFailureOnce()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("race");
        var concurrentUpdate = new ConcurrentVersionBump(sql);
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email), fakeClock: true,
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(concurrentUpdate)));
        factory.Clock.UtcNow = WholeSecondsNow();
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        var versionBefore = await sql.ScalarAsync<int>(DataScope.Identity, "SELECT Version FROM dbo.Users WHERE Id = @id", ("@id", userId));

        var body = await FailureBodyAsync(client, email, WrongPassword);

        Assert.Contains("invalid_credentials", body, StringComparison.Ordinal);
        Assert.Equal(1, concurrentUpdate.Fired);
        Assert.Equal((1, (DateTime?)null), Counts(await CredentialStateAsync(sql, userId)));
        Assert.Equal(versionBefore + 2, await sql.ScalarAsync<int>(DataScope.Identity, "SELECT Version FROM dbo.Users WHERE Id = @id", ("@id", userId)));
    }

    /// <summary>NFR4: wrong password, unknown email and locked take comparable time (one hash verify each); generous bound.</summary>
    [Fact]
    [Trait("Category", "Timing")]
    public async Task Login_WhenWrongUnknownOrLocked_TakesComparableTime()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("timingwrong");
        var lockedEmail = UniqueEmail("timinglocked");
        var settings = Seed(email);
        settings["EventHub:Seed:SystemAdministrators:1:Email"] = lockedEmail;
        settings["EventHub:Seed:SystemAdministrators:1:DevPassword"] = Password;
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: settings, fakeClock: true);
        factory.Clock.UtcNow = WholeSecondsNow();
        using var client = factory.CreateApiClient();
        await LockAsync(client, lockedEmail);
        var wrongUserId = await UserIdAsync(sql, email);
        var cases = new[] { (email, WrongPassword), (UniqueEmail("timingunknown"), Password), (lockedEmail, Password) };

        const int tries = 9;
        var samples = cases.Select(_ => new List<double>()).ToArray();
        for (var round = -1; round < tries; round++)
        {
            for (var i = 0; i < cases.Length; i++)
            {
                // Keep the wrong-password account below the lockout threshold (outside the measurement).
                await sql.ExecuteAsync(DataScope.Identity, "UPDATE dbo.Users SET AccessFailedCount = 0 WHERE Id = @id", ("@id", wrongUserId));
                var started = Stopwatch.GetTimestamp();
                var response = await LoginAsync(client, cases[i].Item1, cases[i].Item2);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                if (round >= 0)
                {
                    samples[i].Add(elapsed);
                }
            }
        }

        var medians = samples.Select(Median).ToArray();
        var ratio = medians.Max() / medians.Min();
        Assert.True(ratio < 3.0, $"Median ms wrong/unknown/locked = {string.Join(" / ", medians.Select(m => m.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))} (ratio {ratio:0.00}).");
    }

    private EventHubApiFactory Factory(string email)
    {
        var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email), fakeClock: true);
        factory.Clock.UtcNow = WholeSecondsNow();
        return factory;
    }

    private static DateTime WholeSecondsNow()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    private static async Task LockAsync(HttpClient client, string email)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await FailureBodyAsync(client, email, WrongPassword);
        }
    }

    private static (int, DateTime?) Counts((int AccessFailedCount, DateTime? LockoutEndUtc, string Status) state) =>
        (state.AccessFailedCount, state.LockoutEndUtc);

    /// <summary>Asserts a 401 <c>invalid_credentials</c> without a cookie and returns the body with the traceId blanked.</summary>
    private static async Task<string> FailureBodyAsync(HttpClient client, string email, string password)
    {
        var response = await LoginAsync(client, email, password);
        Assert.Null(SessionSetCookie(response));
        return await BodyOf(response);
    }

    private static async Task<string> BodyOf(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{response.StatusCode}: {raw}");
        using (var problem = JsonDocument.Parse(raw))
        {
            Assert.Equal("invalid_credentials", problem.RootElement.GetProperty("code").GetString());
        }

        var traceId = JsonNode.Parse(raw)!["traceId"]!.GetValue<string>();
        return raw.Replace(traceId, "<traceId>", StringComparison.Ordinal);
    }

    /// <summary>
    /// Once, right before the first save that changes a user's failure count, bumps that row's <c>Version</c> on
    /// another connection, as a concurrent request would.
    /// </summary>
    private sealed class ConcurrentVersionBump(LocalSqlFixture sql) : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entry = eventData.Context?.ChangeTracker.Entries<User>()
                .FirstOrDefault(user => user.State == EntityState.Modified && user.Property("AccessFailedCount").IsModified);
            if (entry is not null && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
            {
                await sql.ExecuteAsync(DataScope.Identity, "UPDATE dbo.Users SET Version = Version + 1 WHERE Id = @id", ("@id", entry.Entity.Id));
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }
}
#endif
