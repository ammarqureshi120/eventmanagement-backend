using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace EventHub.Api.IntegrationTests.Features.Seeding;

/// <summary>FR37 / AD-16 / AD-26: System Administrators come only from configuration, idempotently, with no password.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class SystemAdministratorSeedingTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Start_WhenPlainEmailConfiguredAndApiStartsTwice_CreatesOneActivePasswordlessSysAdmin()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("sara");
        var settings = new Dictionary<string, string> { ["EventHub:Seed:SystemAdministrators:0"] = email };

        await StartAsync(settings);
        await StartAsync(settings);

        var rows = await ReadUsers(email);
        var row = Assert.Single(rows);
        Assert.Equal("SystemAdministrator", row.Role);
        Assert.Equal("Active", row.Status);
        Assert.Null(row.OrganizationId);
        Assert.Null(row.PasswordHash);
        Assert.Equal(string.Empty, row.FirstName);
        Assert.Equal(string.Empty, row.LastName);
        Assert.Equal(email, row.Email);
    }

    [Fact]
    public async Task Start_WhenObjectEntryConfigured_UsesTheConfiguredNames()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("khan");

        await StartAsync(new Dictionary<string, string>
        {
            ["EventHub:Seed:SystemAdministrators:0:Email"] = email,
            ["EventHub:Seed:SystemAdministrators:0:FirstName"] = "Sara",
            ["EventHub:Seed:SystemAdministrators:0:LastName"] = "Khan",
        });

        var row = Assert.Single(await ReadUsers(email));
        Assert.Equal("Sara", row.FirstName);
        Assert.Equal("Khan", row.LastName);
    }

    [Fact]
    public async Task Start_WhenEntriesDifferByCaseOrAreBlankOrLaterRemoved_SeedsOnceWarnsWithoutEmailAndKeepsRows()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("edge");
        var longNameEmail = UniqueEmail("longname");
        var sink = new InMemoryLogSink();

        await StartAsync(
            new Dictionary<string, string>
            {
                ["EventHub:Seed:SystemAdministrators:0"] = email,
                ["EventHub:Seed:SystemAdministrators:1"] = email.ToUpperInvariant(),
                ["EventHub:Seed:SystemAdministrators:2"] = " ",
                ["EventHub:Seed:SystemAdministrators:3"] = "not-an-email",
                ["EventHub:Seed:SystemAdministrators:4:Email"] = longNameEmail,
                ["EventHub:Seed:SystemAdministrators:4:FirstName"] = new string('a', 101),
            },
            sink);

        var original = Assert.Single(await ReadUsers(email));
        Assert.Empty(await ReadUsers(longNameEmail));
        var warnings = sink.Events.Where(e => e.Level == LogEventLevel.Warning).Select(InMemoryLogSink.Render).ToList();
        Assert.Contains(warnings, w => w.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, w => w.Contains("blank", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, w => w.Contains("entry 3", StringComparison.Ordinal) && w.Contains("not a valid", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("entry 4", StringComparison.Ordinal) && w.Contains("not a valid", StringComparison.Ordinal));
        var lines = sink.RenderAll();
        Assert.DoesNotContain(lines, line => line.Contains(email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains(longNameEmail, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("not-an-email", StringComparison.OrdinalIgnoreCase));

        // The email is later removed from configuration: its row stays exactly as it was.
        var other = UniqueEmail("other");
        await StartAsync(new Dictionary<string, string> { ["EventHub:Seed:SystemAdministrators:0"] = other });

        var after = Assert.Single(await ReadUsers(email));
        Assert.Equal(original, after);
        Assert.Single(await ReadUsers(other));
    }

    [Fact]
    public async Task Start_WhenEmailHasAnApostrophe_SeedsIt()
    {
        sql.SkipIfUnavailable();
        var email = $"o'neil-{Guid.NewGuid():N}@example.test";

        await StartAsync(new Dictionary<string, string> { ["EventHub:Seed:SystemAdministrators:0"] = email });

        Assert.Equal(email, Assert.Single(await ReadUsers(email)).Email);
    }

    [Fact]
    public async Task Users_WhenRowsBreakTheRoleOrganizationCheckOrTheEmailIndex_AreRejected()
    {
        sql.SkipIfUnavailable();
        var sysAdminWithOrg = await Assert.ThrowsAsync<SqlException>(() =>
            InsertUser(Guid.NewGuid(), Guid.NewGuid(), "SystemAdministrator", UniqueEmail("check1")));
        Assert.Equal(547, sysAdminWithOrg.Number);
        Assert.Contains("CK_Users_Role_OrganizationId", sysAdminWithOrg.Message, StringComparison.Ordinal);

        var orgUserWithoutOrg = await Assert.ThrowsAsync<SqlException>(() =>
            InsertUser(Guid.NewGuid(), null, "OrgAdministrator", UniqueEmail("check2")));
        Assert.Equal(547, orgUserWithoutOrg.Number);

        var email = UniqueEmail("unique");
        await InsertUser(Guid.NewGuid(), null, "SystemAdministrator", email);
        var duplicate = await Assert.ThrowsAsync<SqlException>(() =>
            InsertUser(Guid.NewGuid(), null, "SystemAdministrator", email.ToUpperInvariant()));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("UX_Users_NormalizedEmail", duplicate.Message, StringComparison.Ordinal);
    }

#if EVENTHUB_SEED_TOOLS
    [Fact]
    public async Task SetPasswordForSeed_WhenCalled_StoresOnlyAStrongOneWayHash()
    {
        sql.SkipIfUnavailable();
        const string password = "Seed-Passw0rd-1!";
        var email = UniqueEmail("hash");
        await using var factory = new EventHubApiFactory(
            sql.ConnectionString, settings: new Dictionary<string, string> { ["EventHub:Seed:SystemAdministrators:0"] = email });
        using var _ = factory.CreateClient();
        var before = Assert.Single(await ReadUsers(email));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIdentityAccount>()
                .SetPasswordForSeedAsync(before.Id, password, TestContext.Current.CancellationToken);
        }

        var after = Assert.Single(await ReadUsers(email));
        Assert.NotNull(after.PasswordHash);
        Assert.DoesNotContain(password, after.PasswordHash, StringComparison.Ordinal);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        // ASP.NET Identity v3 format: PBKDF2 with HMAC-SHA512, 100k iterations, random salt.
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(null!, after.PasswordHash!, password));
        Assert.Equal(0x01, Convert.FromBase64String(after.PasswordHash!)[0]);
    }

    /// <summary>Story 1.4 decision: a Development-only seed password, applied only to a user without one, never logged.</summary>
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task Start_WhenDevPasswordConfigured_SetsItOnlyInDevelopmentAndNeverLogsIt(string environment, bool expectPassword)
    {
        sql.SkipIfUnavailable();
        const string devPassword = "Dev-Seed-Passw0rd-7c1";
        var email = UniqueEmail("devpw");
        var sink = new InMemoryLogSink();

        await StartAsync(
            new Dictionary<string, string>
            {
                ["EventHub:Seed:SystemAdministrators:0:Email"] = email,
                ["EventHub:Seed:SystemAdministrators:0:DevPassword"] = devPassword,
            },
            sink,
            environment);

        var row = Assert.Single(await ReadUsers(email));
        if (expectPassword)
        {
            Assert.NotNull(row.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success,
                new PasswordHasher<User>().VerifyHashedPassword(null!, row.PasswordHash!, devPassword));
        }
        else
        {
            Assert.Null(row.PasswordHash);
            Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning
                                              && InMemoryLogSink.Render(e).Contains("not in Development", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(sink.RenderAll(), line => line.Contains(devPassword, StringComparison.Ordinal));
        Assert.DoesNotContain(sink.RenderAll(), line => line.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Start_WhenUserAlreadyHasAPassword_LeavesItUnchanged()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("haspw");
        Dictionary<string, string> Settings(string password) => new()
        {
            ["EventHub:Seed:SystemAdministrators:0:Email"] = email,
            ["EventHub:Seed:SystemAdministrators:0:DevPassword"] = password,
        };

        await StartAsync(Settings("First-Passw0rd-1"));
        var first = Assert.Single(await ReadUsers(email));
        await StartAsync(Settings("Second-Passw0rd-2"));
        var second = Assert.Single(await ReadUsers(email));

        Assert.NotNull(first.PasswordHash);
        Assert.Equal(first.PasswordHash, second.PasswordHash);
        Assert.Equal(first.SecurityStamp, second.SecurityStamp);
    }

    [Fact]
    public async Task Start_WhenExistingPasswordlessUserGetsADevPassword_SetsIt()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("later");
        await StartAsync(new Dictionary<string, string> { ["EventHub:Seed:SystemAdministrators:0"] = email });
        Assert.Null(Assert.Single(await ReadUsers(email)).PasswordHash);

        await StartAsync(new Dictionary<string, string>
        {
            ["EventHub:Seed:SystemAdministrators:0:Email"] = email,
            ["EventHub:Seed:SystemAdministrators:0:DevPassword"] = "Later-Passw0rd-3",
        });

        Assert.NotNull(Assert.Single(await ReadUsers(email)).PasswordHash);
    }
#endif

    private async Task StartAsync(Dictionary<string, string> settings, InMemoryLogSink? sink = null, string environment = "Development")
    {
        await using var factory = new EventHubApiFactory(
            sql.ConnectionString,
            environment,
            settings: settings,
            configureServices: sink is null ? null : services => services.AddSingleton<ILogEventSink>(sink));
        using var _ = factory.CreateClient(); // starts the host: migrator, then seeder
    }

    private async Task<List<UserRow>> ReadUsers(string email)
    {
        await using var connection = await sql.OpenAsync(DataScope.Identity, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT Id, OrganizationId, Role, Status, FirstName, LastName, Email, PasswordHash, SecurityStamp, UpdatedAtUtc " +
            "FROM dbo.Users WHERE NormalizedEmail = @normalized", connection);
        command.Parameters.AddWithValue("@normalized", User.NormalizeEmail(email));
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<UserRow>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new UserRow(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetDateTime(9)));
        }

        return rows;
    }

    private Task<int> InsertUser(Guid id, Guid? organizationId, string role, string email) =>
        sql.ExecuteAsync(
            DataScope.System,
            "INSERT INTO dbo.Users (Id, OrganizationId, Role, Status, FirstName, LastName, Email, NormalizedEmail, Version, " +
            "CreatedAtUtc, UpdatedAtUtc, AccessFailedCount, LockoutEnabled) " +
            "VALUES (@id, @org, @role, 'Active', N'', N'', @email, @normalized, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 1)",
            ("@id", id), ("@org", organizationId), ("@role", role), ("@email", email), ("@normalized", User.NormalizeEmail(email)));

    private static string UniqueEmail(string name) => $"{name}-{Guid.NewGuid():N}@example.test";

    private sealed record UserRow(
        Guid Id,
        Guid? OrganizationId,
        string Role,
        string Status,
        string FirstName,
        string LastName,
        string Email,
        string? PasswordHash,
        string? SecurityStamp,
        DateTime UpdatedAtUtc);
}
