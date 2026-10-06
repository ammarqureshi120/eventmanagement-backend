using System.Text.RegularExpressions;
using EventHub.Application.Audit;
using EventHub.Application.Audit.EventHandlers;
using EventHub.Application.Common.Ports;
using EventHub.Contracts.Audit;
using EventHub.Domain.Audit;
using EventHub.Domain.Common;
using EventHub.Domain.Users;

namespace EventHub.Application.Tests.Pipeline;

/// <summary>AD-14: one entry per event; actor, id, time and visibility resolved by the single writer.</summary>
public sealed partial class AuditWriterTests
{
    private static readonly DateTime Now = new(2027, 5, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid ActorOrg = Guid.NewGuid();
    private static readonly Guid EventOrg = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenPlatformAction_WritesOnePlatformEntryWithIdClockAndActor()
    {
        var (db, writer, id) = Arrange(ActorKind.SystemAdministrator, organizationId: null);

        await writer.Handle(new Sample(AuditAction.OrganizationCreated, EventOrg), TestContext.Current.CancellationToken);

        var entry = Assert.Single(db.Added.OfType<AuditEntry>());
        Assert.Equal(id, entry.Id);
        Assert.Equal(Now, entry.OccurredAtUtc);
        Assert.Equal("organization.created", entry.Action);
        Assert.Equal(AuditEntryVisibility.Platform, entry.Visibility);
        Assert.Equal(EventOrg, entry.OrganizationId);
        Assert.Equal(AuditActorType.SystemAdministrator, entry.ActorType);
        Assert.Equal(ActorId, entry.ActorId);
        Assert.Equal("Actor Name", entry.ActorName);
        Assert.Equal("Sample", entry.EntityType);
    }

    [Fact]
    public async Task Handle_WhenTenantAction_WritesTenantEntryForTheEntityOrganization()
    {
        var (db, writer, _) = Arrange(ActorKind.OrgAdministrator, ActorOrg);

        await writer.Handle(new Sample(AuditAction.EventPublished, EventOrg), TestContext.Current.CancellationToken);

        var entry = Assert.Single(db.Added.OfType<AuditEntry>());
        Assert.Equal(AuditEntryVisibility.Tenant, entry.Visibility);
        Assert.Equal(EventOrg, entry.OrganizationId);
        Assert.Equal(AuditActorType.User, entry.ActorType);
    }

    [Theory]
    [InlineData(UserRole.OrgAdministrator, AuditEntryVisibility.Platform)]
    [InlineData(UserRole.EventManager, AuditEntryVisibility.Tenant)]
    [InlineData(UserRole.SystemAdministrator, AuditEntryVisibility.Platform)]
    public async Task Handle_WhenUserLifecycleAction_FollowsTheTargetRole(UserRole targetRole, AuditEntryVisibility expected)
    {
        var (db, writer, _) = Arrange(ActorKind.OrgAdministrator, ActorOrg);

        await writer.Handle(new Sample(AuditAction.UserDeactivated, EventOrg, targetRole), TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(db.Added.OfType<AuditEntry>()).Visibility);
    }

    [Fact]
    public async Task Handle_WhenUserLifecycleActionLacksTargetRole_Throws()
    {
        var (_, writer, _) = Arrange(ActorKind.OrgAdministrator, ActorOrg);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.Handle(new Sample(AuditAction.UserInvited, EventOrg), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_WhenSignedInBySystemAdministrator_IsPlatformWithNullOrganization()
    {
        var (db, writer, _) = Arrange(ActorKind.SystemAdministrator, organizationId: null);

        await writer.Handle(new Sample(AuditAction.UserSignedIn, null), TestContext.Current.CancellationToken);

        var entry = Assert.Single(db.Added.OfType<AuditEntry>());
        Assert.Equal(AuditEntryVisibility.Platform, entry.Visibility);
        Assert.Null(entry.OrganizationId);
    }

    [Fact]
    public async Task Handle_WhenSignedInByOrganizationUser_IsTenantInTheActorsOrganization()
    {
        var (db, writer, _) = Arrange(ActorKind.EventManager, ActorOrg);

        await writer.Handle(new Sample(AuditAction.UserSignedOut, null), TestContext.Current.CancellationToken);

        var entry = Assert.Single(db.Added.OfType<AuditEntry>());
        Assert.Equal(AuditEntryVisibility.Tenant, entry.Visibility);
        Assert.Equal(ActorOrg, entry.OrganizationId);
    }

    [Fact]
    public async Task Handle_WhenSystemActor_RecordsSystemWithoutActorId()
    {
        var (db, writer, _) = Arrange(ActorKind.System, organizationId: null);

        await writer.Handle(new Sample(AuditAction.EventRegistrationOpened, EventOrg), TestContext.Current.CancellationToken);

        var entry = Assert.Single(db.Added.OfType<AuditEntry>());
        Assert.Equal(AuditActorType.System, entry.ActorType);
        Assert.Null(entry.ActorId);
    }

    [Fact]
    public async Task Handle_WhenAnonymous_Throws()
    {
        var (db, writer, _) = Arrange(ActorKind.Anonymous, organizationId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.Handle(new Sample(AuditAction.VenueCreated, EventOrg), TestContext.Current.CancellationToken));
        Assert.Empty(db.Added);
    }

    [Fact]
    public async Task Handle_WhenAnonymousCallerAndEventCarriesASysAdminSubject_RecordsTheSubjectAsPlatformActor()
    {
        var (db, writer, _) = Arrange(ActorKind.Anonymous, organizationId: null);
        var subjectId = Guid.NewGuid();

        await writer.Handle(
            new Sample(AuditAction.UserSignedIn, null) { Subject = new AuditSubjectActor(subjectId, ActorKind.SystemAdministrator, null, "Sara Khan") },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(db.Added.OfType<AuditEntry>());
        Assert.Equal(AuditActorType.SystemAdministrator, entry.ActorType);
        Assert.Equal(subjectId, entry.ActorId);
        Assert.Equal("Sara Khan", entry.ActorName);
        Assert.Equal(AuditEntryVisibility.Platform, entry.Visibility);
        Assert.Null(entry.OrganizationId);
    }

    [Fact]
    public async Task Handle_WhenEventCarriesASubject_TheSubjectWinsOverTheCurrentUser()
    {
        var (db, writer, _) = Arrange(ActorKind.SystemAdministrator, organizationId: null);
        var subjectId = Guid.NewGuid();

        await writer.Handle(
            new Sample(AuditAction.UserSignedIn, null) { Subject = new AuditSubjectActor(subjectId, ActorKind.EventManager, ActorOrg, "Em Manager") },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(db.Added.OfType<AuditEntry>());
        Assert.Equal(AuditActorType.User, entry.ActorType);
        Assert.Equal(subjectId, entry.ActorId);
        Assert.Equal("Em Manager", entry.ActorName);
        Assert.Equal(AuditEntryVisibility.Tenant, entry.Visibility);
        Assert.Equal(ActorOrg, entry.OrganizationId);
    }

    [Fact]
    public async Task Handle_WhenAnonymousCallerAndNoSubject_StillThrows()
    {
        var (db, writer, _) = Arrange(ActorKind.Anonymous, organizationId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.Handle(new Sample(AuditAction.UserSignedIn, null), TestContext.Current.CancellationToken));
        Assert.Empty(db.Added);
    }

    [Fact]
    public async Task Handle_WhenSubjectIsAnonymous_Throws()
    {
        var (db, writer, _) = Arrange(ActorKind.SystemAdministrator, organizationId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.Handle(
                new Sample(AuditAction.UserSignedIn, null) { Subject = new AuditSubjectActor(Guid.NewGuid(), ActorKind.Anonymous, null, "x") },
                TestContext.Current.CancellationToken));
        Assert.Empty(db.Added);
    }

    [Fact]
    public async Task Handle_WhenTenantVisibilityResolvesWithoutOrganization_Throws()
    {
        var (tenantDb, tenantWriter, _) = Arrange(ActorKind.OrgAdministrator, ActorOrg);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await tenantWriter.Handle(new Sample(AuditAction.VenueCreated, null), TestContext.Current.CancellationToken));
        Assert.Empty(tenantDb.Added);

        // ByActor for an actor with no Organization (System) and an event without one.
        var (systemDb, systemWriter, _) = Arrange(ActorKind.System, organizationId: null);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await systemWriter.Handle(new Sample(AuditAction.UserSignedIn, null), TestContext.Current.CancellationToken));
        Assert.Empty(systemDb.Added);

        // ByTargetRole for a non-admin target without an Organization.
        var (targetDb, targetWriter, _) = Arrange(ActorKind.SystemAdministrator, organizationId: null);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await targetWriter.Handle(new Sample(AuditAction.UserEdited, null, UserRole.EventManager), TestContext.Current.CancellationToken));
        Assert.Empty(targetDb.Added);
    }

    [Theory]
    [InlineData(201, 201)]
    [InlineData(260, 201)]
    public async Task Handle_WhenActorNameIsAFullName_StoresUpToTheColumnLength(int nameLength, int storedLength)
    {
        var db = new FakeDb();
        var name = new string('n', nameLength / 2) + " " + new string('m', nameLength - (nameLength / 2) - 1);
        var writer = new AuditWriter(db, new FakeUser(ActorKind.OrgAdministrator, ActorOrg, name), new FixedIds(Guid.NewGuid()), new FixedClock());

        await writer.Handle(new Sample(AuditAction.EventPublished, EventOrg), TestContext.Current.CancellationToken);

        Assert.Equal(storedLength, Assert.Single(db.Added.OfType<AuditEntry>()).ActorName.Length);
    }

    [Fact]
    public void Catalogue_WhenRead_GivesEveryActionAUniqueWireValueAndARule()
    {
        var wireValues = Enum.GetValues<AuditAction>().Select(AuditActionCatalogue.WireValue).ToList();

        Assert.Equal(wireValues.Count, wireValues.Distinct(StringComparer.Ordinal).Count());
        Assert.All(wireValues, value => Assert.Matches(WirePattern(), value));
        Assert.All(Enum.GetValues<AuditAction>(), action => Assert.True(Enum.IsDefined(AuditActionCatalogue.VisibilityRule(action))));
        Assert.Equal(AuditVisibilityRule.ByActor, AuditActionCatalogue.VisibilityRule(AuditAction.UserSignedIn));
        Assert.Equal("user.signedIn", AuditActionCatalogue.WireValue(AuditAction.UserSignedIn));
        Assert.Equal("\"attendees.exported\"", System.Text.Json.JsonSerializer.Serialize(AuditAction.AttendeesExported));
    }

    private static (FakeDb Db, AuditWriter Writer, Guid Id) Arrange(ActorKind kind, Guid? organizationId)
    {
        var db = new FakeDb();
        var id = Guid.NewGuid();
        var writer = new AuditWriter(db, new FakeUser(kind, organizationId), new FixedIds(id), new FixedClock());
        return (db, writer, id);
    }

    [GeneratedRegex("^[a-z]+[A-Za-z]*\\.[a-z][A-Za-z]*$")]
    private static partial Regex WirePattern();

    private sealed record Sample(AuditAction Action, Guid? OrganizationId, UserRole? TargetRole = null) : IAuditableEvent
    {
        public string EntityType => "Sample";

        public Guid EntityId { get; } = Guid.NewGuid();

        public AuditSubjectActor? Subject { get; init; }

        public AuditSubjectActor? SubjectActor => Subject;
    }

    private sealed class FakeDb : IAppDbContext
    {
        public List<object> Added { get; } = [];

        public bool HasActiveTransaction => false;

        public bool HasPendingChanges => false;

        public void Add<TEntity>(TEntity entity)
            where TEntity : class => Added.Add(entity);

        public IReadOnlyList<IHasDomainEvents> GetAggregatesWithEvents() => [];

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IAppTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUser(ActorKind kind, Guid? organizationId, string displayName = "Actor Name") : ICurrentUser
    {
        public ActorKind Kind => kind;

        public Guid? UserId => kind is ActorKind.Anonymous ? null : ActorId;

        public string DisplayName => displayName;

        public Guid? OrganizationId => organizationId;
    }

    private sealed class FixedIds(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => Now;
    }
}
