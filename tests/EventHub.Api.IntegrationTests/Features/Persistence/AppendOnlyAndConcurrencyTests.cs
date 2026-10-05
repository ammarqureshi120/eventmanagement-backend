using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Domain.Audit;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Identity;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Api.IntegrationTests.Features.Persistence;

/// <summary>AD-14: Audit Entries are append-only. AD-24: the Identity store bumps User.Version and detects stale writes.</summary>
[Collection(LocalSqlCollection.Name)]
public sealed class AppendOnlyAndConcurrencyTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task AuditEntries_WhenModifiedOrDeleted_SaveThrows()
    {
        sql.SkipIfUnavailable();
        var entry = AuditEntry.Record(
            Guid.NewGuid(), Guid.NewGuid(), AuditActorType.System, null, "System", "event.published", "Event",
            Guid.NewGuid(), AuditEntryVisibility.Tenant, DateTime.UtcNow);
        await using (var insert = sql.CreateContext(DataScope.System))
        {
            insert.AuditEntries.Add(entry);
            await insert.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var modify = sql.CreateContext(DataScope.System);
        var loaded = await modify.AuditEntries.SingleAsync(e => e.Id == entry.Id, TestContext.Current.CancellationToken);
        modify.Entry(loaded).Property(e => e.ActorName).CurrentValue = "Someone else";
        await Assert.ThrowsAsync<InvalidOperationException>(() => modify.SaveChangesAsync(TestContext.Current.CancellationToken));

        await using var delete = sql.CreateContext(DataScope.System);
        delete.AuditEntries.Remove(await delete.AuditEntries.SingleAsync(e => e.Id == entry.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => delete.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("System", await sql.ScalarAsync<string>(
            DataScope.System, "SELECT ActorName FROM dbo.AuditEntries WHERE Id = @id", ("@id", entry.Id)));
    }

    [Fact]
    public async Task UserStoreUpdate_WhenTwoContextsUpdateTheSameUser_BumpsVersionAndTheSecondGetsConcurrencyFailure()
    {
        sql.SkipIfUnavailable();
        var user = User.CreateSystemAdministrator(Guid.NewGuid(), $"race-{Guid.NewGuid():N}@example.test", null, null);
        user.CreatedAtUtc = user.UpdatedAtUtc = DateTime.UtcNow; // the test context has no timestamps interceptor
        await using (var create = sql.CreateContext(DataScope.Identity))
        {
            create.Users.Add(user);
            await create.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var firstDb = sql.CreateContext(DataScope.Identity);
        await using var secondDb = sql.CreateContext(DataScope.Identity);
        using var first = new UserStore(firstDb);
        using var second = new UserStore(secondDb);
        var firstCopy = (await first.FindByIdAsync(user.Id.ToString(), TestContext.Current.CancellationToken))!;
        var secondCopy = (await second.FindByIdAsync(user.Id.ToString(), TestContext.Current.CancellationToken))!;

        await first.SetSecurityStampAsync(firstCopy, "stamp-1", TestContext.Current.CancellationToken);
        var firstResult = await first.UpdateAsync(firstCopy, TestContext.Current.CancellationToken);
        await second.SetSecurityStampAsync(secondCopy, "stamp-2", TestContext.Current.CancellationToken);
        var secondResult = await second.UpdateAsync(secondCopy, TestContext.Current.CancellationToken);

        Assert.True(firstResult.Succeeded);
        Assert.False(secondResult.Succeeded);
        Assert.Equal(new IdentityErrorDescriber().ConcurrencyFailure().Code, Assert.Single(secondResult.Errors).Code);
        Assert.Equal(2, await sql.ScalarAsync<int>(DataScope.System, "SELECT Version FROM dbo.Users WHERE Id = @id", ("@id", user.Id)));
        Assert.Equal("stamp-1", await sql.ScalarAsync<string>(DataScope.System, "SELECT SecurityStamp FROM dbo.Users WHERE Id = @id", ("@id", user.Id)));
    }
}
