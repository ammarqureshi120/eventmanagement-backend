using EventHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Infrastructure.Persistence.Configurations.Users;

/// <summary>Names of the Identity credential columns, mapped as EF shadow properties on <c>Users</c> (AD-26).</summary>
public static class UserCredentialProperties
{
    public const string PasswordHash = "PasswordHash";
    public const string SecurityStamp = "SecurityStamp";
    public const string LockoutEndUtc = "LockoutEndUtc";
    public const string AccessFailedCount = "AccessFailedCount";
    public const string LockoutEnabled = "LockoutEnabled";
}

/// <summary>
/// <c>Users</c>: the domain <see cref="User"/> plus credential shadow columns that only the Identity stores
/// touch (AD-26). Unique normalized email platform-wide and the SysAdmin ⇔ no Organization CHECK (AD-19).
/// The Organization FK arrives with Organizations (Story 2.1).
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string NormalizedEmailIndex = "UX_Users_NormalizedEmail";
    public const string RoleOrganizationCheck = "CK_Users_Role_OrganizationId";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", table => table.HasCheckConstraint(
            RoleOrganizationCheck,
            "([Role] = 'SystemAdministrator' AND [OrganizationId] IS NULL) OR ([Role] <> 'SystemAdministrator' AND [OrganizationId] IS NOT NULL)"));

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.FirstName).HasMaxLength(User.NameMaxLength).IsRequired();
        builder.Property(user => user.LastName).HasMaxLength(User.NameMaxLength).IsRequired();
        builder.Property(user => user.Phone).HasMaxLength(User.PhoneMaxLength);
        builder.Property(user => user.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName(NormalizedEmailIndex);

        builder.Property(user => user.Version).IsConcurrencyToken();

        builder.Ignore(user => user.DomainEvents);

        builder.Property<string?>(UserCredentialProperties.PasswordHash).HasMaxLength(512).IsUnicode(false);
        builder.Property<string?>(UserCredentialProperties.SecurityStamp).HasMaxLength(64).IsUnicode(false);
        builder.Property<DateTime?>(UserCredentialProperties.LockoutEndUtc);
        builder.Property<int>(UserCredentialProperties.AccessFailedCount);
        builder.Property<bool>(UserCredentialProperties.LockoutEnabled);
    }
}
