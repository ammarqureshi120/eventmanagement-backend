using EventHub.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventHub.Infrastructure.Persistence.Configurations.Audit;

/// <summary>
/// <c>AuditEntries</c>: append-only (AD-14). <c>Visibility</c> is varchar(16) to match <c>sec.fn_audit</c>.
/// List indexes arrive with audit history (Story 5.5).
/// </summary>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.ActorName).HasMaxLength(AuditEntry.ActorNameMaxLength).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsUnicode(false).IsRequired();
        builder.Property(entry => entry.EntityType).HasMaxLength(AuditEntry.EntityTypeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(entry => entry.Visibility).HasMaxLength(16);
    }
}
