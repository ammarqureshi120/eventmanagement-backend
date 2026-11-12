#if EVENTHUB_SEED_TOOLS
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventHub.Api.Hosting;
using EventHub.Api.IntegrationTests.Features.Auth;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Me;

/// <summary>
/// Story 1.7a (FR36, FR32, AD-16, AD-17, AD-24): <c>PUT /api/me/profile</c> and <c>POST /api/me/change-password</c>.
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class MyAccountTests(LocalSqlFixture sql)
{
    private const string NewPassword = "Battery-Staple-42";
    private const string WrongPassword = "Wrong-Passw0rd";
    private static readonly Uri Origin = new("http://localhost");

    // ---- Profile ----

    [Fact]
    public async Task UpdateProfile_WhenValid_Returns204ChangesOnlyNamesAndPhoneBumpsVersionAndAuditsOnceWithMeAsActor()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("profile");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        var response = await client.PutAsJsonAsync("/api/me/profile", new
        {
            firstName = "  Sarah ",
            lastName = "Khan",
            phone = " +31 (0)6-1234.5678 ",
            version = before.Version,
            // Never changed through this endpoint (ignored).
            email = "someone-else@example.test",
            role = "orgAdministrator",
            status = "inactive",
            organizationId = Guid.NewGuid(),
        }, TestContext.Current.CancellationToken);

        await AssertStatusAsync(HttpStatusCode.NoContent, response);
        var after = await RowAsync(userId);
        Assert.Equal(("Sarah", "Khan", "+31 (0)6-1234.5678"), (after.FirstName, after.LastName, after.Phone));
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal((email, "SystemAdministrator", "Active", (Guid?)null), (after.Email, after.Role, after.Status, after.OrganizationId));
        Assert.Equal((before.PasswordHash, before.SecurityStamp), (after.PasswordHash, after.SecurityStamp));

        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.edited"));
        Assert.Equal(1, await sql.ScalarAsync<int>(
            DataScope.System,
            "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @id AND Action = 'user.edited' AND ActorId = @id " +
            "AND ActorType = 'SystemAdministrator' AND Visibility = 'Platform' AND OrganizationId IS NULL AND EntityType = 'User'",
            ("@id", userId)));

        using var me = await GetMeAsync(client);
        Assert.Equal("Sarah", me.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("+31 (0)6-1234.5678", me.RootElement.GetProperty("phone").GetString());
        Assert.Equal(after.Version, me.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task UpdateProfile_WhenPhoneIsBlank_ClearsIt()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("phoneclear");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        await AssertStatusAsync(HttpStatusCode.NoContent, await PutProfileAsync(client, FirstName, LastName, "0612345678", (await RowAsync(userId)).Version));

        await AssertStatusAsync(HttpStatusCode.NoContent, await PutProfileAsync(client, FirstName, LastName, "   ", (await RowAsync(userId)).Version));

        Assert.Null((await RowAsync(userId)).Phone);
        Assert.Equal(2, await AuditCountAsync(sql, userId, "user.edited"));
    }

    [Fact]
    public async Task UpdateProfile_WhenInvalid_Returns400ValidationOnFirstNameLastNameAndPhoneAndSavesNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("profileinvalid");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        var response = await PutProfileAsync(client, "", new string('b', 101), "12ab", before.Version);

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["firstName", "lastName", "phone"], errors.EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Enter your first name.", errors.GetProperty("firstName")[0].GetString());
        Assert.Equal("Use 100 characters or fewer.", errors.GetProperty("lastName")[0].GetString());
        Assert.Equal("Enter a phone number with digits, spaces and + ( ) - . only.", errors.GetProperty("phone")[0].GetString());
        Assert.Equal(before, await RowAsync(userId));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.edited"));
    }

    [Fact]
    public async Task UpdateProfile_WhenVersionIsStale_Returns409ConcurrencyConflictAndSavesNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("stale");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        var response = await PutProfileAsync(client, "Other", "Name", null, before.Version - 1);

        using var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency_conflict");
        Assert.Equal("Someone else changed this. Reload to see the latest.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(before, await RowAsync(userId));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.edited"));
    }

    /// <summary>The explicit version check runs before the no-op check: a stale save is a conflict even with no change.</summary>
    [Fact]
    public async Task UpdateProfile_WhenVersionIsStaleAndNothingChanges_Returns409()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("stalenoop");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        var response = await PutProfileAsync(client, before.FirstName, before.LastName, before.Phone, before.Version - 1);

        (await AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency_conflict")).Dispose();
        Assert.Equal(before, await RowAsync(userId));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.edited"));
    }

    [Fact]
    public async Task UpdateProfile_WhenVersionIsMissing_Returns400ValidationOnVersionAndSavesNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("noversion");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        var response = await client.PutAsJsonAsync(
            "/api/me/profile", new { firstName = "Other", lastName = "Name", phone = (string?)null }, TestContext.Current.CancellationToken);

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(["version"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(before, await RowAsync(userId));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.edited"));
    }

    [Fact]
    public async Task UpdateProfile_WhenNothingChanges_Returns204WithoutAVersionBumpOrAudit()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("noop");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        var response = await PutProfileAsync(client, $" {FirstName} ", LastName, "", before.Version);

        await AssertStatusAsync(HttpStatusCode.NoContent, response);
        Assert.Equal(before, await RowAsync(userId));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.edited"));
    }

    /// <summary>Two saves sent at once with the same version: exactly one wins, the other is a conflict.</summary>
    [Fact]
    public async Task UpdateProfile_WhenTwoSavesRaceWithTheSameVersion_OneIs204AndTheOther409()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("race");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var tabA = factory.CreateApiClient();
        using var tabB = factory.CreateApiClient();
        await SignInAsync(tabA, email);
        await SignInAsync(tabB, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        var responses = await Task.WhenAll(
            PutProfileAsync(tabA, "Alpha", LastName, null, before.Version),
            PutProfileAsync(tabB, "Bravo", LastName, null, before.Version));

        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order());
        var winner = responses[0].StatusCode == HttpStatusCode.NoContent ? "Alpha" : "Bravo";
        var after = await RowAsync(userId);
        Assert.Equal(winner, after.FirstName);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.edited"));
    }

    /// <summary>
    /// The race the version check cannot see: the row changes after the read but before the save. The concurrency token
    /// (the caller's version as the original value) makes the UPDATE match no row: 409, nothing saved, no audit.
    /// </summary>
    [Fact]
    public async Task UpdateProfile_WhenTheRowChangesBetweenReadAndSave_Returns409AndSavesNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("tokenrace");
        var concurrentUpdate = new ConcurrentProfileBump(sql);
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(concurrentUpdate)));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        var response = await PutProfileAsync(client, "Changed", LastName, null, before.Version);

        (await AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency_conflict")).Dispose();
        Assert.Equal(1, concurrentUpdate.Fired);
        var after = await RowAsync(userId);
        Assert.Equal(FirstName, after.FirstName);
        Assert.Equal(before.Version + 1, after.Version); // only the concurrent bump
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.edited"));
    }

    // ---- Change password ----

    [Fact]
    public async Task ChangePassword_WhenCurrentIsRight_KeepsThisSessionWithTheSameIatEndsOthersClearsLockoutAndAuditsOnce()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("changepw");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email), fakeClock: true);
        var signedInAt = WholeSecondsNow();
        factory.Clock.UtcNow = signedInAt;
        using var current = factory.CreateApiClient(out var browser);
        using var other = factory.CreateApiClient();
        using var guesser = factory.CreateApiClient();
        await SignInAsync(current, email);
        await SignInAsync(other, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);
        var oldCookie = browser.Cookie(Origin, AuthSetup.CookieName)!;

        // Someone guessed at sign-in and locked the account; the signed-in session is unaffected.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(guesser, email, WrongPassword)).StatusCode);
        }

        Assert.NotNull((await CredentialStateAsync(sql, userId)).LockoutEndUtc);
        factory.Clock.UtcNow = signedInAt.AddMinutes(10);

        var response = await ChangePasswordAsync(current, Password, NewPassword);

        await AssertStatusAsync(HttpStatusCode.NoContent, response);
        var reissued = SessionSetCookie(response);
        Assert.NotNull(reissued);
        Assert.DoesNotContain("expires=Thu, 01 Jan 1970", reissued, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(oldCookie, browser.Cookie(Origin, AuthSetup.CookieName));

        var after = await RowAsync(userId);
        Assert.NotEqual(before.PasswordHash, after.PasswordHash);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal((0, (DateTime?)null), Counts(await CredentialStateAsync(sql, userId)));
        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.passwordChanged"));
        Assert.Equal(1, await sql.ScalarAsync<int>(
            DataScope.System,
            "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @id AND Action = 'user.passwordChanged' AND ActorId = @id " +
            "AND ActorType = 'SystemAdministrator' AND Visibility = 'Platform' AND OrganizationId IS NULL",
            ("@id", userId)));

        // This session keeps working, still bound to the original sign-in time (12 h absolute limit unchanged).
        factory.Clock.UtcNow = signedInAt.AddMinutes(20);
        using (var me = await GetMeAsync(current))
        {
            Assert.Equal(signedInAt.AddHours(12), me.RootElement.GetProperty("sessionExpiresAt").GetDateTime().ToUniversalTime());
        }

        // Every other session, and a copy of this session's old cookie, ends.
        await MeTests.AssertSessionExpired(await other.GetAsync("/api/me", TestContext.Current.CancellationToken));
        await MeTests.AssertSessionExpired(await GetWithCookieAsync(factory, "/api/me", oldCookie));

        // The new password signs in; the old one does not.
        using var fresh = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(fresh, email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(fresh, email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WhenCurrentIsWrongSixTimes_Returns400OnCurrentPasswordNeverLocksAndChangesNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("wrongcurrent");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        using var other = factory.CreateApiClient();
        await SignInAsync(client, email);
        await SignInAsync(other, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var problem = await AssertProblemAsync(
                await ChangePasswordAsync(client, WrongPassword, NewPassword), HttpStatusCode.BadRequest, "validation");
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal(["currentPassword"], errors.EnumerateObject().Select(error => error.Name));
            Assert.Equal("That's not your current password. Check it and try again.", errors.GetProperty("currentPassword")[0].GetString());
        }

        Assert.Equal(before, await RowAsync(userId));
        Assert.Equal((0, (DateTime?)null), Counts(await CredentialStateAsync(sql, userId)));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.passwordChanged"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
        using var fresh = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(fresh, email, Password)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WhenNewPasswordIsWeak_Returns400OnNewPasswordAndChangesNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("weaknew");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        using var problem = await AssertProblemAsync(
            await ChangePasswordAsync(client, Password, "abc"), HttpStatusCode.BadRequest, "validation");

        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["newPassword"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal(
            ["Use at least 8 characters.", "Include at least one number."],
            errors.GetProperty("newPassword").EnumerateArray().Select(message => message.GetString()));
        Assert.Equal(before, await RowAsync(userId));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.passwordChanged"));
    }

    // ---- Access ----

    [Fact]
    public async Task MyAccountCommands_WhenAnonymous_Return401SessionExpired()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var client = factory.CreateApiClient();

        await MeTests.AssertSessionExpired(await PutProfileAsync(client, "A", "B", null, 1));
        await MeTests.AssertSessionExpired(await ChangePasswordAsync(client, Password, NewPassword));
    }

    [Fact]
    public async Task MyAccountCommands_WhenSignedInWithoutAValidXsrfToken_Return400AndChangeNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("xsrf");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using var client = factory.CreateApiClient();
        await SignInAsync(client, email);
        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/me/profile")
        {
            Content = JsonContent.Create(new { firstName = "X", lastName = "Y", phone = (string?)null, version = before.Version }),
        };
        put.Headers.Add(ApiClient.XsrfHeader, "not-a-token");
        using var post = new HttpRequestMessage(HttpMethod.Post, "/api/me/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = Password, newPassword = NewPassword }),
        };
        post.Headers.Add(ApiClient.XsrfHeader, "not-a-token");

        (await AssertProblemAsync(await client.SendAsync(put, TestContext.Current.CancellationToken), HttpStatusCode.BadRequest, "validation")).Dispose();
        (await AssertProblemAsync(await client.SendAsync(post, TestContext.Current.CancellationToken), HttpStatusCode.BadRequest, "validation")).Dispose();
        Assert.Equal(before, await RowAsync(userId));
    }

    // ---- AD-24 interceptor through the Identity store ----

    /// <summary>The Identity store bumps Version itself; the version interceptor must not add a second bump.</summary>
    [Fact]
    public async Task IdentityStoreUpdate_WhenSavedThroughTheVersionInterceptor_BumpsVersionExactlyOnce()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("onebump");
        await using var factory = new EventHubApiFactory(sql.ConnectionString, settings: Seed(email));
        using (factory.CreateClient())
        {
            // Starting the host runs the seeder.
        }

        var userId = await UserIdAsync(sql, email);
        var before = await RowAsync(userId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIdentityAccount>()
                .RotateSecurityStampAsync(userId, TestContext.Current.CancellationToken);
        }

        var after = await RowAsync(userId);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.Version + 1, after.Version);
    }

    // ---- helpers ----

    private static Task<HttpResponseMessage> PutProfileAsync(HttpClient client, string? firstName, string? lastName, string? phone, int version) =>
        client.PutAsJsonAsync("/api/me/profile", new { firstName, lastName, phone, version }, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string currentPassword, string newPassword) =>
        client.PostAsJsonAsync("/api/me/change-password", new { currentPassword, newPassword }, TestContext.Current.CancellationToken);

    private static async Task<JsonDocument> GetMeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");
        return JsonDocument.Parse(raw);
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == expected, $"{response.StatusCode}: {raw}");
    }

    private static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(raw);
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        return problem;
    }

    private static (int, DateTime?) Counts((int AccessFailedCount, DateTime? LockoutEndUtc, string Status) state) =>
        (state.AccessFailedCount, state.LockoutEndUtc);

    private static DateTime WholeSecondsNow()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    private async Task<UserRow> RowAsync(Guid userId)
    {
        await using var connection = await sql.OpenAsync(DataScope.System, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT FirstName, LastName, Phone, Version, Email, Role, Status, OrganizationId, PasswordHash, SecurityStamp FROM dbo.Users WHERE Id = @id",
            connection);
        command.Parameters.AddWithValue("@id", userId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return new UserRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9));
    }

    private sealed record UserRow(
        string FirstName, string LastName, string? Phone, int Version, string Email, string Role, string Status,
        Guid? OrganizationId, string? PasswordHash, string? SecurityStamp)
    {
        /// <summary>Keeps the hash and stamp out of assertion messages.</summary>
        public override string ToString() => $"UserRow {{ {FirstName} {LastName}, Phone = {Phone}, Version = {Version} }}";
    }

    /// <summary>
    /// Once, right before the first save that edits a user's first name, bumps that row's <c>Version</c> on another
    /// connection, as a concurrent save would.
    /// </summary>
    private sealed class ConcurrentProfileBump(LocalSqlFixture sql) : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entry = eventData.Context?.ChangeTracker.Entries<User>()
                .FirstOrDefault(user => user.State == EntityState.Modified && user.Property(u => u.FirstName).IsModified);
            if (entry is not null && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
            {
                await sql.ExecuteAsync(DataScope.System, "UPDATE dbo.Users SET Version = Version + 1 WHERE Id = @id", ("@id", entry.Entity.Id));
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
#endif
