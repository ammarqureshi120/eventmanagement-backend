using EventHub.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Infrastructure.Persistence.Configurations.Outbox;

/// <summary>
/// <c>OutboxMessages</c> (AD-15): a global System table (no <c>OrganizationId</c>, no RLS predicate). The relay's claim
/// query is served by the filtered index on <c>(NextAttemptAtUtc, LeaseUntilUtc)</c> over pending rows only.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const string PendingIndex = "IX_OutboxMessages_NextAttemptAtUtc_LeaseUntilUtc";

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.Type).HasMaxLength(OutboxMessage.TypeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(message => message.Payload);
        builder.Property(message => message.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength).IsUnicode(false);

        builder.HasIndex(message => new { message.NextAttemptAtUtc, message.LeaseUntilUtc })
            .HasDatabaseName(PendingIndex)
            .HasFilter("[ProcessedAtUtc] IS NULL");
    }
}
