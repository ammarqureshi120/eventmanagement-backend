using EventHub.Domain.Users;
using EventHub.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Infrastructure.Persistence.Configurations.Identity;

/// <summary>
/// <c>UserTokens</c> (AD-16): a global table (no <c>OrganizationId</c>, no RLS predicate; reached only in the Identity
/// scope by token hash or user id). The hash is <c>binary(32)</c> under the unique index <c>UX_UserTokens_TokenHash</c>.
/// </summary>
public sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public const string TokenHashIndex = "UX_UserTokens_TokenHash";

    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("UserTokens");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.TokenHash).HasMaxLength(UserToken.HashBytes).IsFixedLength().IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName(TokenHashIndex);

        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
