using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Audit;

namespace RushDay.Infrastructure.Persistence.Configurations;

/// <summary>
/// Append-only. No foreign keys on purpose: an audit row must outlive the module or user it names,
/// and the application has no code path that updates or deletes these rows; the migration adds the
/// trg_audit_events_immutable trigger so the database refuses them too.
/// </summary>
internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ActorUsername).HasMaxLength(64);
        builder.Property(a => a.ActorRole).HasMaxLength(16);
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.SubjectType).HasMaxLength(32).IsRequired();
        builder.Property(a => a.SubjectId).HasMaxLength(64);
        builder.Property(a => a.Details).HasColumnType("jsonb");
        builder.Property(a => a.RequestId).HasMaxLength(64);
        builder.Property(a => a.IpHash).HasMaxLength(64);
        builder.Property(a => a.ChainHash).HasMaxLength(64);

        builder.HasIndex(a => a.OccurredAt).IsDescending();
        builder.HasIndex(a => new { a.StudentId, a.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(a => new { a.ModuleId, a.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(a => new { a.ActorUserId, a.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(a => new { a.Action, a.OccurredAt }).IsDescending(false, true);
    }
}
