using EventHub.Application.Authorization;
using EventHub.Application.Common.Ports;
using EventHub.Application.Common.Tenancy;
using EventHub.Application.Common.Validation;

namespace EventHub.Application.Tests.Pipeline;

/// <summary>AD-8 matrix (deny by default), AD-7 scope derivation and AD-17 error-key paths.</summary>
public sealed class PermissionAndValidationTests
{
    private static readonly Permission Sample = new("Sample.Run");

    [Fact]
    public void Matrix_WhenNoSourceGrants_DeniesEverything()
    {
        var matrix = new PermissionMatrix([new EmptyPermissionGrantSource()]);

        Assert.False(matrix.IsGranted(ActorKind.SystemAdministrator, Sample, ScopeKind.Platform));
        Assert.False(matrix.IsGranted(ActorKind.System, Sample, ScopeKind.System));
    }

    [Fact]
    public void Matrix_WhenGranted_AllowsOnlyThatActorPermissionAndScope()
    {
        var matrix = new PermissionMatrix([new Grants(new PermissionGrant(ActorKind.OrgAdministrator, Sample, ScopeKind.Tenant))]);

        Assert.True(matrix.IsGranted(ActorKind.OrgAdministrator, Sample, ScopeKind.Tenant));
        Assert.False(matrix.IsGranted(ActorKind.EventManager, Sample, ScopeKind.Tenant));
        Assert.False(matrix.IsGranted(ActorKind.OrgAdministrator, Sample, ScopeKind.Platform));
        Assert.False(matrix.IsGranted(ActorKind.OrgAdministrator, new Permission("Sample.Other"), ScopeKind.Tenant));
    }

    [Fact]
    public void Matrix_WhenAnonymousOrNoScope_DeniesEvenIfAGrantClaimsIt()
    {
        var matrix = new PermissionMatrix([new Grants(
            new PermissionGrant(ActorKind.Anonymous, Sample, ScopeKind.None),
            new PermissionGrant(ActorKind.SystemAdministrator, Sample, ScopeKind.None))]);

        Assert.False(matrix.IsGranted(ActorKind.Anonymous, Sample, ScopeKind.None));
        Assert.False(matrix.IsGranted(ActorKind.SystemAdministrator, Sample, ScopeKind.None));
    }

    [Fact]
    public void Matrix_WhenPermissionIsDefault_NeverMatchesAndRejectsSuchGrants()
    {
        var matrix = new PermissionMatrix([new Grants(new PermissionGrant(ActorKind.OrgAdministrator, Sample, ScopeKind.Tenant))]);

        Assert.False(matrix.IsGranted(ActorKind.OrgAdministrator, default, ScopeKind.Tenant));
        Assert.Throws<ArgumentException>(() =>
            new PermissionMatrix([new Grants(new PermissionGrant(ActorKind.OrgAdministrator, default, ScopeKind.Tenant))]));
    }

    [Theory]
    [InlineData(ActorKind.OrgAdministrator, "00000000-0000-0000-0000-000000000000", ScopeKind.None)]
    [InlineData(ActorKind.SystemAdministrator, null, ScopeKind.Platform)]
    [InlineData(ActorKind.OrgAdministrator, "1b8e5f7e-0000-0000-0000-000000000001", ScopeKind.Tenant)]
    [InlineData(ActorKind.EventManager, "1b8e5f7e-0000-0000-0000-000000000001", ScopeKind.Tenant)]
    [InlineData(ActorKind.OrgAdministrator, null, ScopeKind.None)]
    [InlineData(ActorKind.System, null, ScopeKind.System)]
    [InlineData(ActorKind.Anonymous, null, ScopeKind.None)]
    public void TenantContext_WhenDerivedFromTheCaller_MapsToTheAd7Scope(ActorKind kind, string? organization, ScopeKind expected)
    {
        var organizationId = organization is null ? (Guid?)null : Guid.Parse(organization);
        var context = new CurrentUserTenantContext(new Caller(kind, organizationId));

        Assert.Equal(expected, context.Scope);
        Assert.Equal(expected == ScopeKind.Tenant ? organizationId : null, context.OrganizationId);
    }

    [Theory]
    [InlineData("Name", "name")]
    [InlineData("Attendees[3].Email", "attendees.3.email")]
    [InlineData("Contact.EmailAddress", "contact.emailAddress")]
    [InlineData("Items[0].Tags[12]", "items.0.tags.12")]
    [InlineData("URL", "url")]
    [InlineData("IDNumber", "idNumber")]
    [InlineData("", "")]
    public void ToPath_WhenGivenAFluentValidationPath_ReturnsTheCamelCaseDotPath(string input, string expected)
    {
        Assert.Equal(expected, CamelCasePathResolver.ToPath(input));
    }

    private sealed class Grants(params PermissionGrant[] grants) : IPermissionGrantSource
    {
        IEnumerable<PermissionGrant> IPermissionGrantSource.Grants => grants;
    }

    private sealed class Caller(ActorKind kind, Guid? organizationId) : ICurrentUser
    {
        public ActorKind Kind => kind;

        public Guid? UserId => null;

        public string DisplayName => string.Empty;

        public Guid? OrganizationId => organizationId;
    }
}
