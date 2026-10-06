using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Infrastructure.Persistence.Rls;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Api.IntegrationTests.Isolation;

/// <summary>AD-30 / NFR21: the foundation migration's RLS policy, RCSI, model drift and the AddTenantRls helper.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class RlsCoverageTests(LocalSqlFixture sql)
{
    private const string PolicyPredicatesSql = """
        SELECT CONCAT(
                   OBJECT_NAME(p.target_object_id) COLLATE DATABASE_DEFAULT, ':',
                   p.predicate_type_desc COLLATE DATABASE_DEFAULT, ':',
                   COALESCE(p.operation_desc COLLATE DATABASE_DEFAULT, ''), ':',
                   p.predicate_definition COLLATE DATABASE_DEFAULT)
          FROM sys.security_predicates p
          JOIN sys.security_policies sp ON sp.object_id = p.object_id
         WHERE SCHEMA_NAME(sp.schema_id) = N'sec' AND sp.name = N'TenantIsolation'
        """;

    [Fact]
    public async Task Database_WhenMigrated_HasEveryOrganizationIdTableUnderTheSecurityPolicy()
    {
        sql.SkipIfUnavailable();

        var uncovered = await QueryStrings("""
            SELECT t.name
              FROM sys.tables t
              JOIN sys.columns c ON c.object_id = t.object_id AND c.name = N'OrganizationId'
             WHERE t.is_ms_shipped = 0
               AND NOT EXISTS (
                   SELECT 1
                     FROM sys.security_predicates p
                     JOIN sys.security_policies sp ON sp.object_id = p.object_id
                    WHERE SCHEMA_NAME(sp.schema_id) = N'sec' AND sp.name = N'TenantIsolation'
                      AND p.target_object_id = t.object_id AND p.predicate_type_desc = N'FILTER')
            """);

        Assert.Empty(uncovered);
        Assert.True(await sql.ScalarAsync<bool>(
            DataScope.None, "SELECT is_enabled FROM sys.security_policies WHERE name = N'TenantIsolation'"));
        Assert.Equal(
            [
                // Story 1.4: only AFTER INSERT allows Identity-scope inserts (its own actions); UPDATE and reads stay on fn_audit.
                "AuditEntries:BLOCK:AFTER INSERT:([sec].[fn_audit_write]([OrganizationId],[Visibility],[Action]))",
                "AuditEntries:BLOCK:AFTER UPDATE:([sec].[fn_audit]([OrganizationId],[Visibility]))",
                "AuditEntries:FILTER::([sec].[fn_audit]([OrganizationId],[Visibility]))",
                "Users:BLOCK:AFTER INSERT:([sec].[fn_users]([OrganizationId]))",
                "Users:BLOCK:AFTER UPDATE:([sec].[fn_users]([OrganizationId]))",
                "Users:FILTER::([sec].[fn_users]([OrganizationId]))",
            ],
            (await QueryStrings(PolicyPredicatesSql)).Where(p => !p.StartsWith("RlsProbe_", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Database_WhenMigrated_HasReadCommittedSnapshotAndNoPendingModelChanges()
    {
        sql.SkipIfUnavailable();
        await using var db = sql.CreateContext();

        Assert.True(await sql.ScalarAsync<bool>(
            DataScope.None, "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()"));
        Assert.False(db.Database.HasPendingModelChanges(), "The model has changes without a migration (AD-30).");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            ["Auth_Users", "Foundation_Rls", "Security_DataProtectionKeys", "Auth_AuditIdentityInsert"],
            (await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Select(id => id[15..]));
    }

    [Fact]
    public async Task Database_WhenMigrated_HasOnlyTheAllowListedGlobalTablesOutsideTheSecurityPolicy()
    {
        sql.SkipIfUnavailable();

        // AD-7 / AD-21: a new global (non-RLS) table must be added here on purpose, never by accident.
        var global = await QueryStrings("""
            SELECT t.name
              FROM sys.tables t
             WHERE t.is_ms_shipped = 0
               AND NOT EXISTS (
                   SELECT 1
                     FROM sys.security_predicates p
                     JOIN sys.security_policies sp ON sp.object_id = p.object_id
                    WHERE SCHEMA_NAME(sp.schema_id) = N'sec' AND sp.name = N'TenantIsolation'
                      AND p.target_object_id = t.object_id)
            """);

        Assert.Equal(
            ["DataProtectionKeys", "__EFMigrationsHistory"],
            global.Except(CreatedProbeTables, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(0, await sql.ScalarAsync<int>(
            DataScope.None, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DataProtectionKeys') AND name = N'OrganizationId'"));
    }

    public static TheoryData<string> AnyScope => ["none", "identity", "platform", "tenant"];

    /// <summary>AD-21: the key ring loads in whatever scope the Data Protection repository's DI scope resolves to.</summary>
    [Theory]
    [MemberData(nameof(AnyScope))]
    public async Task DataProtectionKeys_WhenWrittenAndReadInAnyDataScope_IsVisible(string scopeName)
    {
        sql.SkipIfUnavailable();
        var scope = scopeName switch
        {
            "identity" => DataScope.Identity,
            "platform" => DataScope.Platform,
            "tenant" => DataScope.Tenant(Guid.NewGuid()),
            _ => DataScope.None,
        };
        var friendlyName = $"rls-probe-{Guid.NewGuid():N}";
        var ct = TestContext.Current.CancellationToken;

        // The probe row never reaches the shared key ring: the transaction is rolled back.
        await using var connection = await sql.OpenAsync(scope, ct);
        await using var transaction = connection.BeginTransaction();
        await using (var insert = new SqlCommand(
                         "INSERT INTO dbo.DataProtectionKeys (FriendlyName, Xml) VALUES (@name, N'<probe />')", connection, transaction))
        {
            insert.Parameters.AddWithValue("@name", friendlyName);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await using (var read = new SqlCommand(
                         "SELECT COUNT(*) FROM dbo.DataProtectionKeys WHERE FriendlyName = @name", connection, transaction))
        {
            read.Parameters.AddWithValue("@name", friendlyName);
            Assert.Equal(1, (int)(await read.ExecuteScalarAsync(ct))!);
        }

        await transaction.RollbackAsync(ct);
        Assert.Equal(0, await sql.ScalarAsync<int>(
            DataScope.None, "SELECT COUNT(*) FROM dbo.DataProtectionKeys WHERE FriendlyName = @name", ("@name", friendlyName)));
    }

    /// <summary>Exact names of the probe tables created below; only these are excluded from the global allow-list.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> CreatedProbeTables = [];

    [Fact]
    public async Task AddTenantRls_WhenAppliedToATenantTable_FiltersAndBlocksOtherScopesThenDropsCleanly()
    {
        sql.SkipIfUnavailable();
        var table = $"RlsProbe_{Guid.NewGuid():N}";
        CreatedProbeTables.Add(table);
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await sql.ExecuteAsync(DataScope.None, $"CREATE TABLE dbo.[{table}] (Id uniqueidentifier NOT NULL PRIMARY KEY, OrganizationId uniqueidentifier NOT NULL)");
        try
        {
            await sql.ExecuteAsync(DataScope.None, TenantRlsMigrationExtensions.AddTenantRlsSql(table));

            var predicates = (await QueryStrings(PolicyPredicatesSql)).Where(p => p.StartsWith(table, StringComparison.Ordinal)).ToList();
            Assert.Equal(3, predicates.Count);
            Assert.Contains(predicates, p => p.Contains(":BLOCK:AFTER INSERT:", StringComparison.Ordinal));
            Assert.Contains(predicates, p => p.Contains(":BLOCK:AFTER UPDATE:", StringComparison.Ordinal));

            var insert = $"INSERT INTO dbo.[{table}] (Id, OrganizationId) VALUES (NEWID(), @org)";
            await sql.ExecuteAsync(DataScope.Tenant(orgA), insert, ("@org", orgA));
            var blocked = await Assert.ThrowsAsync<SqlException>(() => sql.ExecuteAsync(DataScope.Tenant(orgA), insert, ("@org", orgB)));
            Assert.Equal(33504, blocked.Number);
            await Assert.ThrowsAsync<SqlException>(() => sql.ExecuteAsync(DataScope.Platform, insert, ("@org", orgA)));

            var count = $"SELECT COUNT(*) FROM dbo.[{table}]";
            Assert.Equal(1, await sql.ScalarAsync<int>(DataScope.Tenant(orgA), count));
            Assert.Equal(0, await sql.ScalarAsync<int>(DataScope.Tenant(orgB), count));
            Assert.Equal(0, await sql.ScalarAsync<int>(DataScope.Platform, count));
            Assert.Equal(0, await sql.ScalarAsync<int>(DataScope.Identity, count));
            Assert.Equal(0, await sql.ScalarAsync<int>(DataScope.None, count));
            Assert.Equal(1, await sql.ScalarAsync<int>(DataScope.System, count));
        }
        finally
        {
            await sql.ExecuteAsync(DataScope.None,
                $"IF EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.[{table}]')) " +
                TenantRlsMigrationExtensions.DropTenantRlsSql(table));
            await sql.ExecuteAsync(DataScope.None, $"DROP TABLE dbo.[{table}]");
        }

        Assert.DoesNotContain(await QueryStrings(PolicyPredicatesSql), p => p.StartsWith(table, StringComparison.Ordinal));
        Assert.Equal(0, await sql.ScalarAsync<int>(
            DataScope.None, "SELECT COUNT(*) FROM sys.tables WHERE name = @name", ("@name", table)));
    }

    [Theory]
    [InlineData("Users; DROP TABLE x")]
    [InlineData("1Users")]
    [InlineData("")]
    public void AddTenantRlsSql_WhenTableIsNotAPlainIdentifier_Throws(string table)
    {
        Assert.Throws<ArgumentException>(() => TenantRlsMigrationExtensions.AddTenantRlsSql(table));
    }

    private async Task<List<string>> QueryStrings(string query)
    {
        await using var connection = await sql.OpenAsync(DataScope.None, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
