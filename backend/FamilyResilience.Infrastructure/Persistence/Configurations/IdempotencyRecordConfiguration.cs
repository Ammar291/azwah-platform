using FamilyResilience.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys", table =>
            table.HasCheckConstraint("ck_idempotency_keys_expiry", "expires_at > created_at"));
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Subject).HasMaxLength(IdempotencyRecord.MaxSubjectLength).IsRequired();
        builder.Property(record => record.Operation).HasMaxLength(IdempotencyRecord.MaxOperationLength).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasIndex(record => new { record.Subject, record.Key }, "ux_idempotency_keys_subject_key")
            .HasDatabaseName("ux_idempotency_keys_subject_key")
            .IsUnique();
        builder.HasIndex(record => record.ExpiresAt, "ix_idempotency_keys_expires_at")
            .HasDatabaseName("ix_idempotency_keys_expires_at");
    }
}
