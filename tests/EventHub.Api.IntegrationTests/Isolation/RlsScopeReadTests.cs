using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Api.IntegrationTests.Isolation;

/// <summary>
/// SM-2 start / AD-7: what each scope reads from <c>Users</c> and <c>AuditEntries</c> through direct SQL with the
/// session context the interceptor sets, plus the EF path through the interceptor and <see cref="DbScopeFactory"/>.
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class RlsScopeReadTests(LocalSqlFixture sql)
{
    private static readonly Guid OrgA = Guid.Parse("a0a0a0a0-0000-0000-0000-00000000000a");
    private static readonly Guid OrgB = Guid.Parse("b0b0b0b0-0000-0000-0000-00000000000b");

    // Arranged per test: Users = SysAdmin (no org), OrgAdmin (org A), EventManager (org B);
    // AuditEntries = A/Platform, A/Tenant, B/Tenant, none/Platform.
    [Theory]
    [InlineData("identity", 3, 0)]
    [InlineData("platform", 3, 2)]
    [InlineData("tenantA", 1, 2)]
    [InlineData("tenantB", 1, 1)]
    [InlineData("system", 3, 4)]
    [InlineData("none", 0, 0)]
    public async Task DirectSql_WhenReadInScope_ReturnsOnlyWhatTheScopeAllows(string scopeName, int users, int auditEntries)
    {
        sql.SkipIfUnavailable();
        var marker = await ArrangeAsync();
        var scope = Scope(scopeName);

        Assert.Equal(users, await sql.ScalarAsync<int>(
            scope, "SELECT COUNT(*) FROM dbo.Users WHERE Email LIKE @pattern", ("@pattern", $"%{marker:N}%")));
        Assert.Equal(auditEntries, await sql.ScalarAsync<int>(
            scope, "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @marker", ("@marker", marker)));
    }

    [Fact]
    public async Task PlatformScope_WhenReadingAudit_SeesOnlyPlatformVisibility()
    {
        sql.SkipIfUnavailable();
        var marker = await ArrangeAsync();

        Assert.Equal(0, await sql.ScalarAsync<int>(
            DataScope.Platform,
            "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @marker AND Visibility <> 'Platform'",
            ("@marker", marker)));
    }

    [Fact]
    public async Task EfContext_WhenOpenedInAScope_GetsItsSessionContextFromTheInterceptor()
    {
        sql.SkipIfUnavailable();
        var marker = await ArrangeAsync();
        var pattern = marker.ToString("N");

        await using (var identity = sql.CreateContext(DataScope.Identity))
        {
            Assert.Equal(3, await identity.Users.CountAsync(u => u.Email.Contains(pattern), TestContext.Current.CancellationToken));
        }

        await using (var tenant = sql.CreateContext(DataScope.Tenant(OrgA)))
        {
            Assert.Equal(1, await tenant.Users.CountAsync(u => u.Email.Contains(pattern), TestContext.Current.CancellationToken));
        }

        await using (var unscoped = sql.CreateContext())
        {
            Assert.Equal(0, await unscoped.Users.CountAsync(u => u.Email.Contains(pattern), TestContext.Current.CancellationToken));
            Assert.Equal(0, await unscoped.AuditEntries.CountAsync(a => a.EntityId == marker, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task DbScopeFactory_WhenOpeningEachScope_GivesEachItsOwnContextAndSessionContext()
    {
        sql.SkipIfUnavailable();
        var marker = await ArrangeAsync();
        await using var factory = new EventHubApiFactory(sql.ConnectionString);
        using var _ = factory.CreateClient();
        var scopes = factory.Services.GetRequiredService<DbScopeFactory>();

        await using var platform = scopes.OpenPlatform();
        await using var identity = scopes.OpenIdentity();
        await using var tenant = scopes.OpenTenant(OrgB);
        await using var system = scopes.OpenSystem();

        Assert.NotSame(platform.Db, identity.Db);
        Assert.Equal(2, await platform.Db.AuditEntries.CountAsync(a => a.EntityId == marker, TestContext.Current.CancellationToken));
        Assert.Equal(0, await identity.Db.AuditEntries.CountAsync(a => a.EntityId == marker, TestContext.Current.CancellationToken));
        Assert.Equal(1, await tenant.Db.AuditEntries.CountAsync(a => a.EntityId == marker, TestContext.Current.CancellationToken));
        Assert.Equal(4, await system.Db.AuditEntries.CountAsync(a => a.EntityId == marker, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TenantScope_WhenWritingAnotherOrganizationsRows_IsBlocked()
    {
        sql.SkipIfUnavailable();
        var marker = await ArrangeAsync();
        var tenantA = DataScope.Tenant(OrgA);

        var userInsert = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => sql.ExecuteAsync(
            tenantA,
            "INSERT INTO dbo.Users (Id, OrganizationId, Role, Status, FirstName, LastName, Email, NormalizedEmail, Version, " +
            "CreatedAtUtc, UpdatedAtUtc, AccessFailedCount, LockoutEnabled) " +
            "VALUES (NEWID(), @org, 'EventManager', 'Active', N'', N'', @email, UPPER(@email), 1, SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 1)",
            ("@org", OrgB), ("@email", $"intruder-{marker:N}@example.test")));
        Assert.Equal(33504, userInsert.Number);

        var auditInsert = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => sql.ExecuteAsync(
            tenantA,
            "INSERT INTO dbo.AuditEntries (Id, OrganizationId, ActorType, ActorId, ActorName, Action, EntityType, EntityId, Visibility, OccurredAtUtc) " +
            "VALUES (NEWID(), @org, 'User', NULL, N'x', 'event.published', 'Probe', @marker, 'Tenant', SYSUTCDATETIME())",
            ("@org", OrgB), ("@marker", marker)));
        Assert.Equal(33504, auditInsert.Number);

        // Rows visible to tenant A cannot be moved into another Organization.
        var userUpdate = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => sql.ExecuteAsync(
            tenantA, "UPDATE dbo.Users SET OrganizationId = @org WHERE Email = @email", ("@org", OrgB), ("@email", $"oa-{marker:N}@example.test")));
        Assert.Equal(33504, userUpdate.Number);
        var auditUpdate = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => sql.ExecuteAsync(
            tenantA, "UPDATE dbo.AuditEntries SET OrganizationId = @org WHERE EntityId = @marker", ("@org", OrgB), ("@marker", marker)));
        Assert.Equal(33504, auditUpdate.Number);

        Assert.Equal(1, await sql.ScalarAsync<int>(
            DataScope.System, "SELECT COUNT(*) FROM dbo.Users WHERE Email LIKE @pattern AND OrganizationId = @org", ("@pattern", $"%{marker:N}%"), ("@org", OrgB)));
        Assert.Equal(1, await sql.ScalarAsync<int>(
            DataScope.System, "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @marker AND OrganizationId = @org", ("@marker", marker), ("@org", OrgB)));
    }

    /// <summary>
    /// AD-7 (Story 1.4): the Identity scope may insert only its own Audit Entry actions (sign-in/out, reset completed,
    /// invite accepted) and still reads none, so it can neither see nor update them; other scopes keep their 1.3 rules.
    /// </summary>
    [Fact]
    public async Task IdentityScope_WhenWritingAudit_CanInsertButReadsAndUpdatesNothing()
    {
        sql.SkipIfUnavailable();
        var marker = Guid.NewGuid();
        const string insertAudit =
            "INSERT INTO dbo.AuditEntries (Id, OrganizationId, ActorType, ActorId, ActorName, Action, EntityType, EntityId, Visibility, OccurredAtUtc) " +
            "VALUES (NEWID(), @org, 'SystemAdministrator', @marker, N'x', 'user.signedIn', 'User', @marker, @visibility, SYSUTCDATETIME())";

        Assert.Equal(1, await sql.ExecuteAsync(DataScope.Identity, insertAudit, ("@org", null), ("@marker", marker), ("@visibility", "Platform")));
        Assert.Equal(1, await sql.ExecuteAsync(DataScope.Identity, insertAudit, ("@org", OrgA), ("@marker", marker), ("@visibility", "Tenant")));

        Assert.Equal(0, await sql.ScalarAsync<int>(
            DataScope.Identity, "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @marker", ("@marker", marker)));
        Assert.Equal(0, await sql.ExecuteAsync(
            DataScope.Identity, "UPDATE dbo.AuditEntries SET ActorName = N'changed' WHERE EntityId = @marker", ("@marker", marker)));
        Assert.Equal(2, await sql.ScalarAsync<int>(
            DataScope.System, "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @marker AND ActorName = N'x'", ("@marker", marker)));

        // Any other action is blocked for Identity.
        var otherAction = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => sql.ExecuteAsync(
            DataScope.Identity,
            insertAudit.Replace("'user.signedIn'", "'organization.created'", StringComparison.Ordinal),
            ("@org", null), ("@marker", marker), ("@visibility", "Platform")));
        Assert.Equal(33504, otherAction.Number);

        // No scope still inserts nothing.
        var unscoped = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => sql.ExecuteAsync(
            DataScope.None, insertAudit, ("@org", null), ("@marker", marker), ("@visibility", "Platform")));
        Assert.Equal(33504, unscoped.Number);
    }

    private static DataScope Scope(string name) => name switch
    {
        "identity" => DataScope.Identity,
        "platform" => DataScope.Platform,
        "tenantA" => DataScope.Tenant(OrgA),
        "tenantB" => DataScope.Tenant(OrgB),
        "system" => DataScope.System,
        _ => DataScope.None,
    };

    private async Task<Guid> ArrangeAsync()
    {
        var marker = Guid.NewGuid();
        const string insertUser =
            "INSERT INTO dbo.Users (Id, OrganizationId, Role, Status, FirstName, LastName, Email, NormalizedEmail, Version, " +
            "CreatedAtUtc, UpdatedAtUtc, AccessFailedCount, LockoutEnabled) " +
            "VALUES (NEWID(), @org, @role, 'Active', N'', N'', @email, UPPER(@email), 1, SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 1)";
        await sql.ExecuteAsync(DataScope.System, insertUser, ("@org", null), ("@role", "SystemAdministrator"), ("@email", $"sys-{marker:N}@example.test"));
        await sql.ExecuteAsync(DataScope.System, insertUser, ("@org", OrgA), ("@role", "OrgAdministrator"), ("@email", $"oa-{marker:N}@example.test"));
        await sql.ExecuteAsync(DataScope.System, insertUser, ("@org", OrgB), ("@role", "EventManager"), ("@email", $"em-{marker:N}@example.test"));

        const string insertAudit =
            "INSERT INTO dbo.AuditEntries (Id, OrganizationId, ActorType, ActorId, ActorName, Action, EntityType, EntityId, Visibility, OccurredAtUtc) " +
            "VALUES (NEWID(), @org, 'System', NULL, N'System', 'organization.updated', 'Probe', @marker, @visibility, SYSUTCDATETIME())";
        await sql.ExecuteAsync(DataScope.System, insertAudit, ("@org", OrgA), ("@marker", marker), ("@visibility", "Platform"));
        await sql.ExecuteAsync(DataScope.System, insertAudit, ("@org", OrgA), ("@marker", marker), ("@visibility", "Tenant"));
        await sql.ExecuteAsync(DataScope.System, insertAudit, ("@org", OrgB), ("@marker", marker), ("@visibility", "Tenant"));
        await sql.ExecuteAsync(DataScope.System, insertAudit, ("@org", null), ("@marker", marker), ("@visibility", "Platform"));
        return marker;
    }
}
