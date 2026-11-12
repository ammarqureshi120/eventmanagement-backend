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
    public void Deactivate_WhenActive_BecomesInactiveAndRaisesOneUserDeactivated()
    {
        var id = Guid.NewGuid();
        var user = User.CreateSystemAdministrator(id, "sara@example.test", "Sara", "Khan");

        user.Deactivate();

        Assert.Equal(UserStatus.Inactive, user.Status);
        var raised = Assert.IsType<UserDeactivated>(Assert.Single(user.DomainEvents));
        Assert.Equal(new UserDeactivated(id, UserRole.SystemAdministrator, null), raised);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_IsANoOpWithoutASecondEvent()
    {
        var user = User.CreateSystemAdministrator(Guid.NewGuid(), "sara@example.test", null, null);
        user.Deactivate();
        user.ClearDomainEvents();

        user.Deactivate();

        Assert.Equal(UserStatus.Inactive, user.Status);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void EditProfile_WhenValuesChange_TrimsThemRaisesOneUserProfileEditedAndKeepsEverythingElse()
    {
        var id = Guid.NewGuid();
        var user = User.CreateSystemAdministrator(id, "sara@example.test", "Sara", "Khan");

        var changed = user.EditProfile("  Sarah ", " Khan ", "  +31 (0)6-1234.5678 ");

        Assert.True(changed);
        Assert.Equal("Sarah", user.FirstName);
        Assert.Equal("Khan", user.LastName);
        Assert.Equal("+31 (0)6-1234.5678", user.Phone);
        Assert.Equal("sara@example.test", user.Email);
        Assert.Equal(UserRole.SystemAdministrator, user.Role);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.OrganizationId);
        Assert.Equal(1, user.Version); // the persistence interceptor bumps it on save (AD-24)
        var raised = Assert.IsType<UserProfileEdited>(Assert.Single(user.DomainEvents));
        Assert.Equal(new UserProfileEdited(id, UserRole.SystemAdministrator, null), raised);
    }

    [Fact]
    public void EditProfile_WhenNothingChanges_ReturnsFalseWithoutAnEvent()
    {
        var user = User.CreateSystemAdministrator(Guid.NewGuid(), "sara@example.test", "Sara", "Khan");

        Assert.False(user.EditProfile(" Sara ", "Khan", "   "));
        Assert.Empty(user.DomainEvents);
        Assert.Null(user.Phone);
    }

    [Fact]
    public void EditProfile_WhenPhoneIsCleared_ChangesItToNull()
    {
        var user = User.CreateSystemAdministrator(Guid.NewGuid(), "sara@example.test", "Sara", "Khan");
        user.EditProfile("Sara", "Khan", "0612345678");
        user.ClearDomainEvents();

        Assert.True(user.EditProfile("Sara", "Khan", null));
        Assert.Null(user.Phone);
        Assert.Single(user.DomainEvents);
    }

    [Theory]
    [InlineData("", "Khan", null)]
    [InlineData("  ", "Khan", null)]
    [InlineData("Sara", null, null)]
    [InlineData("Sara", "Khan", "12ab")]
    [InlineData("Sara", "Khan", "+-()")]
    [InlineData("Sara", "Khan", "123456789012345678901234567890123")]
    public void EditProfile_WhenInvalid_ThrowsAndChangesNothing(string? firstName, string? lastName, string? phone)
    {
        var user = User.CreateSystemAdministrator(Guid.NewGuid(), "sara@example.test", "Sara", "Khan");

        Assert.Throws<ArgumentException>(() => user.EditProfile(firstName, lastName, phone));
        Assert.Equal("Sara", user.FirstName);
        Assert.Equal("Khan", user.LastName);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void EditProfile_WhenANameIsLongerThan100_Throws()
    {
        var user = User.CreateSystemAdministrator(Guid.NewGuid(), "sara@example.test", "Sara", "Khan");

        Assert.Throws<ArgumentException>(() => user.EditProfile(new string('a', User.NameMaxLength + 1), "Khan", null));
        Assert.True(user.EditProfile(new string('a', User.NameMaxLength), "Khan", null));
    }

    [Theory]
    [InlineData("0612345678", true)]
    [InlineData("+31 (0)6 1234 5678", true)]
    [InlineData("06-1234.5678", true)]
    [InlineData("12345678901234567890123456789012", true)]
    [InlineData("123456789012345678901234567890123", false)]
    [InlineData("12ab", false)]
    [InlineData("+ ( ) - .", false)]
    [InlineData("０６１２", false)] // full-width digits are not ASCII digits
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidPhone_WhenChecked_AllowsOnlyDigitsSpacesAndPunctuationWithADigit(string? phone, bool expected)
    {
        Assert.Equal(expected, User.IsValidPhone(phone));
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
