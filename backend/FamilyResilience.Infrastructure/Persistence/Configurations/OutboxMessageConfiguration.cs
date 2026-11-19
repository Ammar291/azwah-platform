using FamilyResilience.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", table =>
            table.HasCheckConstraint("ck_outbox_messages_retry_count", "retry_count >= 0"));
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Type).HasMaxLength(OutboxMessage.MaxTypeLength).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.LastErrorCode).HasMaxLength(OutboxMessage.MaxErrorCodeLength);
        builder.HasIndex(message => message.OccurredAt, "ix_outbox_messages_pending")
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL");

        // OutboxProcessor claims the oldest Pending message of the types it has handlers for.
        builder.HasIndex(message => new { message.Type, message.OccurredAt }, "ix_outbox_messages_pending_type_occurred_at")
            .HasDatabaseName("ix_outbox_messages_pending_type_occurred_at")
            .HasFilter("status = 'Pending'");
    }
}
