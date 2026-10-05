using EventHub.Domain.Audit;
using EventHub.Domain.Users;

namespace EventHub.Domain.Tests.Users;

/// <summary>AD-26 / FR37: the seeded System Administrator shape; AD-14: audit entries are well-formed.</summary>
public sealed class UserTests
{
    [Fact]
    public void CreateSystemAdministrator_WhenNamesMissing_IsActiveWithoutOrganizationAndEmptyNames()
    {
        var id = Guid.NewGuid();

        var user = User.CreateSystemAdministrator(id, "  Sara@Example.Test ", null, null);

        Assert.Equal(id, user.Id);
        Assert.Equal(UserRole.SystemAdministrator, user.Role);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.OrganizationId);
        Assert.Equal("Sara@Example.Test", user.Email);
        Assert.Equal("SARA@EXAMPLE.TEST", user.NormalizedEmail);
        Assert.Equal(string.Empty, user.FirstName);
        Assert.Equal(string.Empty, user.LastName);
        Assert.Equal(1, user.Version);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void CreateSystemAdministrator_WhenNamesGiven_TrimsThem()
    {
        var user = User.CreateSystemAdministrator(Guid.NewGuid(), "sara@example.test", " Sara ", " Khan ");

        Assert.Equal("Sara", user.FirstName);
        Assert.Equal("Khan", user.LastName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@example.test")]
    [InlineData("sara@")]
    [InlineData("sa ra@example.test")]
    [InlineData("a@b@c")]
    public void CreateSystemAdministrator_WhenEmailInvalid_Throws(string email)
    {
        Assert.Throws<ArgumentException>(() => User.CreateSystemAdministrator(Guid.NewGuid(), email, null, null));
    }

    [Fact]
    public void CreateSystemAdministrator_WhenIdEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => User.CreateSystemAdministrator(Guid.Empty, "sara@example.test", null, null));
    }

    [Fact]
    public void NormalizeEmail_WhenNotNfc_MatchesIdentitysUpperInvariantNormalizer()
    {
        const string decomposed = "  José@example.test ";

        Assert.Equal("JOSÉ@EXAMPLE.TEST", User.NormalizeEmail(decomposed));
        Assert.Equal(User.NormalizeEmail("josé@example.test"), User.NormalizeEmail(decomposed));
    }

    [Fact]
    public void AuditEntryRecord_WhenActorNameIsTwoFullNames_Accepts201Characters()
    {
        var name = new string('a', User.NameMaxLength) + " " + new string('b', User.NameMaxLength);

        var entry = AuditEntry.Record(
            Guid.NewGuid(), null, AuditActorType.User, Guid.NewGuid(), name, "event.published", "Event", Guid.NewGuid(),
            AuditEntryVisibility.Tenant, DateTime.UtcNow);

        Assert.Equal(201, entry.ActorName.Length);
    }

    [Fact]
    public void AuditEntryRecord_WhenTimeIsNotUtc_Throws()
    {
        Assert.Throws<ArgumentException>(() => AuditEntry.Record(
            Guid.NewGuid(), null, AuditActorType.System, null, "System", "event.published", "Event", Guid.NewGuid(),
            AuditEntryVisibility.Tenant, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Local)));
    }
}
