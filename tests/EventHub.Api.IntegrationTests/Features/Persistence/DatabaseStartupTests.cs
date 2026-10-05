using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EventHub.Api.IntegrationTests.Features.Persistence;

/// <summary>AD-30 / FR37: on an empty database the hosted migrator runs before the seeder; switched off, nothing is created.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class DatabaseStartupTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Start_WhenDatabaseEmptyAndMigrateOnStartup_AppliesBothMigrationsThenSeeds()
    {
        sql.SkipIfUnavailable();
        var database = await sql.CreateEmptyDatabaseAsync();
        try
        {
            var email = $"fresh-{Guid.NewGuid():N}@example.test";
            await using (var factory = new EventHubApiFactory(sql.ConnectionStringFor(database), settings: new Dictionary<string, string>
                         {
                             ["EventHub:Database:MigrateOnStartup"] = "true",
                             ["EventHub:Seed:SystemAdministrators:0"] = email,
                         }))
            {
                using var _ = factory.CreateClient();
            }

            Assert.Equal(["Auth_Users", "Foundation_Rls"], await Strings(database, DataScope.None,
                "SELECT SUBSTRING(MigrationId, 16, 100) FROM dbo.__EFMigrationsHistory ORDER BY MigrationId"));
            Assert.Equal(["SystemAdministrator"], await Strings(database, DataScope.Identity,
                $"SELECT Role FROM dbo.Users WHERE NormalizedEmail = N'{User.NormalizeEmail(email)}'"));
        }
        finally
        {
            await sql.DropDatabaseAsync(database);
        }
    }

    [Fact]
    public async Task Start_WhenMigrateOnStartupFalse_CreatesNoTables()
    {
        sql.SkipIfUnavailable();
        var database = await sql.CreateEmptyDatabaseAsync();
        try
        {
            await using (var factory = new EventHubApiFactory(sql.ConnectionStringFor(database), settings: new Dictionary<string, string>
                         {
                             ["EventHub:Database:MigrateOnStartup"] = "false",
                         }))
            {
                using var _ = factory.CreateClient();
            }

            Assert.Empty(await Strings(database, DataScope.None, "SELECT name FROM sys.tables"));
        }
        finally
        {
            await sql.DropDatabaseAsync(database);
        }
    }

    [Theory]
    [InlineData(null, "Development", true)]
    [InlineData(null, "Testing", true)]
    [InlineData(null, "Production", false)]
    [InlineData(null, "Staging", false)]
    [InlineData(true, "Production", true)]
    [InlineData(false, "Development", false)]
    public void ShouldMigrate_WhenUnset_IsOnlyOnInDevelopmentOrTesting(bool? configured, string environment, bool expected)
    {
        var options = new DatabaseOptions { MigrateOnStartup = configured };

        Assert.Equal(expected, options.ShouldMigrate(new Environment(environment)));
    }

    private async Task<List<string>> Strings(string database, DataScope scope, string query)
    {
        await using var connection = new SqlConnection(sql.ConnectionStringFor(database));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var context = EventHub.Infrastructure.Persistence.Interceptors.RlsSessionContextInterceptor.CreateCommand(connection, scope))
        {
            if (context is not null)
            {
                await context.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
        }

        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "EventHub.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
