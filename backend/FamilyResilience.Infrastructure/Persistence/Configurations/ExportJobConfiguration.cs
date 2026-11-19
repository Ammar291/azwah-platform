using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("export_jobs", table =>
        {
            table.HasCheckConstraint("ck_export_jobs_attempts", $"attempts BETWEEN 0 AND {ExportJob.MaxAttempts}");
            table.HasCheckConstraint("ck_export_jobs_row_count", "row_count IS NULL OR row_count >= 0");
            table.HasCheckConstraint(
                "ck_export_jobs_status_shape",
                "(status = 'Queued' AND attempts = 0 AND started_at IS NULL AND finished_at IS NULL AND row_count IS NULL AND error_code IS NULL AND expires_at IS NULL)"
                + " OR (status = 'Running' AND attempts >= 1 AND started_at IS NOT NULL AND finished_at IS NULL AND row_count IS NULL AND error_code IS NULL AND expires_at IS NULL)"
                + " OR (status IN ('Completed', 'Expired') AND attempts >= 1 AND finished_at IS NOT NULL AND row_count IS NOT NULL AND expires_at IS NOT NULL AND error_code IS NULL)"
                + " OR (status = 'Failed' AND finished_at IS NOT NULL AND error_code IS NOT NULL AND row_count IS NULL AND expires_at IS NULL)");
        });
        builder.HasKey(job => job.Id);
        builder.Property(job => job.RequestedBy).HasMaxLength(ExportJob.MaxRequesterLength).IsRequired();
        builder.PrimitiveCollection(job => job.Columns)
            .HasField("_columns")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired()
            .ElementType()
            .HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.Property(job => job.InstrumentCode).HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.Property(job => job.ErrorCode).HasMaxLength(ExportJob.MaxErrorCodeLength);
        builder.Ignore(job => job.StorageKey);

        builder.HasOne<Wave>()
            .WithMany()
            .HasForeignKey(job => job.WaveId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(job => new { job.RequestedBy, job.RequestedAt }, "ix_export_jobs_requested_by")
            .HasDatabaseName("ix_export_jobs_requested_by");
        builder.HasIndex(job => job.WaveId, "ix_export_jobs_wave")
            .HasDatabaseName("ix_export_jobs_wave");
        builder.HasIndex(job => job.ExpiresAt, "ix_export_jobs_completed_expires_at")
            .HasDatabaseName("ix_export_jobs_completed_expires_at")
            .HasFilter("status = 'Completed'");
    }
}
