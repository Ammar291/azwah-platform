using FamilyResilience.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ActorType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ActorId).HasMaxLength(AuditEvent.MaxActorIdLength);
        builder.Property(e => e.Action).HasMaxLength(AuditEvent.MaxActionLength).IsRequired();
        builder.Property(e => e.ResourceType).HasMaxLength(AuditEvent.MaxResourceTypeLength).IsRequired();
        builder.Property(e => e.ResourceId).HasMaxLength(AuditEvent.MaxResourceIdLength);
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(AuditEvent.MaxCorrelationIdLength);
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.ResourceType, e.ResourceId });
    }
}
