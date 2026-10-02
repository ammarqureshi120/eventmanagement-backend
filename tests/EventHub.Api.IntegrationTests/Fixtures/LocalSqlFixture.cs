using System.ComponentModel;
using Microsoft.Data.SqlClient;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>
/// AD-31 (no containers): one fresh, uniquely named database per test run on the locally installed
/// SQL Server, dropped afterwards. Server comes from <c>EVENTHUB_TEST_SQL</c> (any database name in it
/// is replaced). When SQL Server is unreachable, dependent tests are skipped with the reason, or fail
/// when <c>EVENTHUB_REQUIRE_SQL=1</c> (set by <c>ci.ps1</c>).
/// </summary>
public sealed class LocalSqlFixture : IAsyncLifetime
{
    public const string ServerVariable = "EVENTHUB_TEST_SQL";

    public const string RequireVariable = "EVENTHUB_REQUIRE_SQL";

    public const string DefaultServer = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly SqlConnectionStringBuilder _server;

    public LocalSqlFixture()
    {
        var raw = Environment.GetEnvironmentVariable(ServerVariable);
        try
        {
            _server = new SqlConnectionStringBuilder(string.IsNullOrWhiteSpace(raw) ? DefaultServer : raw)
            {
                InitialCatalog = "master",
                ConnectTimeout = 5,
            };
        }
        catch (ArgumentException ex)
        {
            _server = new SqlConnectionStringBuilder(DefaultServer) { InitialCatalog = "master", ConnectTimeout = 5 };
            UnavailableReason = $"{ServerVariable} is not a valid connection string ({ex.GetType().Name}).";
        }
        DatabaseName = $"EventHub_Test_{Guid.NewGuid():N}";
    }

    public string DatabaseName { get; }

    /// <summary>Connection string to the per-run test database.</summary>
    public string ConnectionString =>
        new SqlConnectionStringBuilder(_server.ConnectionString) { InitialCatalog = DatabaseName }.ConnectionString;

    public string? UnavailableReason { get; private set; }

    /// <summary>Call first in every test that needs the database.</summary>
    public void SkipIfUnavailable()
    {
        if (UnavailableReason is null)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
        {
            Assert.Fail($"{UnavailableReason} ({RequireVariable}=1, so this fails instead of skipping.)");
        }

        Assert.Skip(UnavailableReason);
    }

    public async ValueTask InitializeAsync()
    {
        if (UnavailableReason is not null)
        {
            return;
        }

        try
        {
            await ExecuteOnMasterAsync($"CREATE DATABASE {Quote(DatabaseName)}");
        }
        catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException or Win32Exception)
        {
            var detail = ex is SqlException sql ? $"SQL error {sql.Number}" : ex.GetType().Name;
            UnavailableReason =
                $"Local SQL Server unreachable via {ServerVariable} or the default ('{_server.DataSource}'): " +
                $"{detail}. Install/start SQL Server 2025 Developer or set {ServerVariable}.";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (UnavailableReason is not null)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        try
        {
            await ExecuteOnMasterAsync(
                $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE {Quote(DatabaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE {Quote(DatabaseName)}; END");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or Win32Exception)
        {
            // Never fail the run on cleanup; leave a trace so the stray database can be dropped by hand.
            Console.Error.WriteLine($"[LocalSqlFixture] Could not drop test database {DatabaseName}: {ex.GetType().Name}.");
        }
    }

    private async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(_server.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    // DatabaseName is generated here (letters, digits, underscore), never user input.
    private static string Quote(string name) => $"[{name}]";
}

/// <summary>All tests that need the database join this collection so they share one test database.</summary>
[CollectionDefinition(Name)]
public sealed class LocalSqlCollection : ICollectionFixture<LocalSqlFixture>
{
    public const string Name = "LocalSql";
}
