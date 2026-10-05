using System.Net;
using System.Net.Http.Json;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Data.SqlClient;

namespace EventHub.Api.IntegrationTests.Features.Audit;

/// <summary>AD-14 / FR32: one Audit Entry per auditable event, in the command transaction, or none at all.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class AuditWriterIntegrationTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task AuditedCommand_WhenItCommits_WritesExactlyOneEntryWithActorVisibilityAndClockTime()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        factory.Clock.UtcNow = new DateTime(2027, 4, 1, 8, 30, 0, DateTimeKind.Utc);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestActor.Header, TestActor.SysAdmin);
        var entityId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            "/api/test/audited", new { entityId, failAfterRaise = false }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var connection = await sql.OpenAsync(DataScope.System, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT Id, OrganizationId, ActorType, ActorId, ActorName, Action, EntityType, Visibility, OccurredAtUtc " +
            "FROM dbo.AuditEntries WHERE EntityId = @entityId", connection);
        command.Parameters.AddWithValue("@entityId", entityId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(Guid.Empty, reader.GetGuid(0));
        Assert.Equal(entityId, reader.GetGuid(1));
        Assert.Equal("SystemAdministrator", reader.GetString(2));
        Assert.Equal(TestActor.SysAdminId, reader.GetGuid(3));
        Assert.Equal("Test SysAdmin", reader.GetString(4));
        Assert.Equal("organization.created", reader.GetString(5));
        Assert.Equal("Organization", reader.GetString(6));
        Assert.Equal("Platform", reader.GetString(7));
        Assert.Equal(factory.Clock.UtcNow, DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken), "Expected exactly one audit entry.");
    }

    [Fact]
    public async Task AuditedCommand_WhenHandlerFailsAfterRaising_WritesNoEntryAndReturns500()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestActor.Header, TestActor.SysAdmin);
        var entityId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            "/api/test/audited", new { entityId, failAfterRaise = true }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await CountEntries(entityId));
    }

    [Fact]
    public async Task AuditedCommand_WhenOrgAdministratorInTenantScope_WritesAUserActorEntry()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = factory.CreateClient();
        var organizationId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestActor.Header, TestActor.OrgAdmin(organizationId));

        var response = await client.PostAsJsonAsync(
            "/api/test/audited", new { entityId = organizationId, failAfterRaise = false }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountEntries(organizationId));
        Assert.Equal("User", await sql.ScalarAsync<string>(
            DataScope.System, "SELECT ActorType FROM dbo.AuditEntries WHERE EntityId = @id", ("@id", organizationId)));
    }

    [Fact]
    public async Task Dispatch_WhenEventsNeverSettle_StopsAfterMaxDepthWithServerError()
    {
        sql.SkipIfUnavailable();
        await using var factory = new EventHubApiFactory(sql.ConnectionString, testApi: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestActor.Header, TestActor.SysAdmin);

        var response = await client.PostAsync("/api/test/endless-events", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("server_error", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private Task<int> CountEntries(Guid entityId) =>
        sql.ScalarAsync<int>(DataScope.System, "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @id", ("@id", entityId));
}
