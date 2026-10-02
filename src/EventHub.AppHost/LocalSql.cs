using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EventHub.AppHost;

/// <summary>Dashboard health for the natively installed SQL Server (no container resource to probe).</summary>
internal sealed class LocalSqlHealthCheck(string? connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("ConnectionStrings:eventhub is not configured.");
        }

        try
        {
            await PingAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (SqlException ex) when (ex.Number == CannotOpenDatabase)
        {
            // SQL Server came up after the AppHost's BeforeStart attempt: create the database now and retry once.
            await LocalSqlDatabase.TryEnsureCreatedAsync(connectionString, cancellationToken);
            try
            {
                SqlConnection.ClearAllPools();
                await PingAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (SqlException retry)
            {
                return HealthCheckResult.Unhealthy($"SQL Server unreachable (error {retry.Number}).");
            }
        }
        catch (SqlException ex)
        {
            return HealthCheckResult.Unhealthy($"SQL Server unreachable (error {ex.Number}).");
        }
    }

    /// <summary>SQL error 4060: cannot open the database requested by the login.</summary>
    private const int CannotOpenDatabase = 4060;

    private async Task PingAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}

/// <summary>Creates the (empty) local dev database if it is missing. Never touches schema.</summary>
internal static class LocalSqlDatabase
{
    public static async Task TryEnsureCreatedAsync(string? connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var target = new SqlConnectionStringBuilder(connectionString);
        var databaseName = target.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        try
        {
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "IF DB_ID(@name) IS NULL BEGIN DECLARE @sql nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@name); EXEC (@sql); END",
                connection);
            command.Parameters.AddWithValue("@name", databaseName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            // SQL Server not running yet: the resource shows Unhealthy on the dashboard instead.
            Console.Error.WriteLine($"[apphost] Could not ensure database '{databaseName}' exists (SQL error {ex.Number}).");
        }
    }
}
