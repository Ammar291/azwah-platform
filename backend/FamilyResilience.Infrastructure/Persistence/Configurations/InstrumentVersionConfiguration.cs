using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class InstrumentVersionConfiguration : IEntityTypeConfiguration<InstrumentVersion>
{
    public void Configure(EntityTypeBuilder<InstrumentVersion> builder)
    {
        builder.ToTable("instrument_versions", table =>
        {
            table.HasCheckConstraint("ck_instrument_versions_version_number", "version_number > 0");
            table.HasCheckConstraint(
                "ck_instrument_versions_approval",
                "status = 'Draft' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND approval_source IS NOT NULL "
                + "AND effective_from IS NOT NULL AND content_reference_ar IS NOT NULL AND content_reference_en IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_instrument_versions_effective_window", "effective_to IS NULL OR effective_to > effective_from");
            table.HasCheckConstraint(
                "ck_instrument_versions_retired", "status <> 'Retired' OR (retired_at IS NOT NULL AND effective_to IS NOT NULL)");
        });
        builder.HasKey(version => version.Id);
        builder.Property(version => version.ContentReferenceAr).HasMaxLength(InstrumentVersion.MaxContentReferenceLength);
        builder.Property(version => version.ContentReferenceEn).HasMaxLength(InstrumentVersion.MaxContentReferenceLength);
        builder.Property(version => version.ApprovedBy).HasMaxLength(Approval.MaxApprovedByLength);
        builder.Property(version => version.ApprovalSource).HasMaxLength(Approval.MaxSourceLength);
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(version => version.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(version => version.Sections)
            .WithOne()
            .HasForeignKey(section => section.InstrumentVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(version => version.Sections).HasField("_sections").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(version => new { version.InstrumentId, version.VersionNumber }, "ux_instrument_versions_instrument_version")
            .HasDatabaseName("ux_instrument_versions_instrument_version")
            .IsUnique();
    }
}
