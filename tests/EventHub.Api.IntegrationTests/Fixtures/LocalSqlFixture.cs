using System.ComponentModel;
using EventHub.Infrastructure.Persistence;
using EventHub.Infrastructure.Persistence.Interceptors;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>
/// AD-31 (no containers): one fresh, uniquely named database per test run on the locally installed
/// SQL Server, migrated once (every migration) and dropped afterwards. Server comes from <c>EVENTHUB_TEST_SQL</c> (any database name in it
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
            return;
        }

        // A migration failure is a real failure: let it fail the collection loudly.
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    /// <summary>A context on the test database with no data scope (RLS returns nothing until a scope is set).</summary>
    public AppDbContext CreateContext(DataScope? scope = null)
    {
        var accessor = new ScopeAccessor();
        if (scope is not null && scope.Kind != EventHub.Application.Common.Ports.ScopeKind.None)
        {
            accessor.Pin(scope);
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new RlsSessionContextInterceptor(accessor))
            .Options;
        return new AppDbContext(options, accessor);
    }

    /// <summary>
    /// Opens a raw connection to the test database with the session context of <paramref name="scope"/>, set
    /// exactly as the RLS interceptor does (<see cref="DataScope.None"/> sets nothing).
    /// </summary>
    public async Task<SqlConnection> OpenAsync(DataScope scope, CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = RlsSessionContextInterceptor.CreateCommand(connection, scope);
        if (command is not null)
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return connection;
    }

    /// <summary>Runs a scalar query in <paramref name="scope"/>; parameters are name/value pairs.</summary>
    public async Task<T> ScalarAsync<T>(DataScope scope, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await OpenAsync(scope);
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Runs a statement in <paramref name="scope"/>; parameters are name/value pairs.</summary>
    public async Task<int> ExecuteAsync(DataScope scope, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await OpenAsync(scope);
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync();
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

    /// <summary>Connection string to another database on the same server (for throwaway databases).</summary>
    public string ConnectionStringFor(string databaseName) =>
        new SqlConnectionStringBuilder(_server.ConnectionString) { InitialCatalog = databaseName }.ConnectionString;

    /// <summary>Creates an extra empty, uniquely named database; drop it with <see cref="DropDatabaseAsync"/>.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"EventHub_Test_{Guid.NewGuid():N}";
        await ExecuteOnMasterAsync($"CREATE DATABASE {Quote(name)}");
        return name;
    }

    public async Task DropDatabaseAsync(string databaseName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(databaseName, "^EventHub_Test_[0-9a-f]{32}$"))
        {
            throw new ArgumentException("Only throwaway test databases can be dropped.", nameof(databaseName));
        }

        SqlConnection.ClearAllPools();
        await ExecuteOnMasterAsync(
            $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE {Quote(databaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE {Quote(databaseName)}; END");
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
