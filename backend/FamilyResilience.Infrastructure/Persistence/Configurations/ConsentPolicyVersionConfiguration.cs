using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class ConsentPolicyVersionConfiguration : IEntityTypeConfiguration<ConsentPolicyVersion>
{
    public void Configure(EntityTypeBuilder<ConsentPolicyVersion> builder)
    {
        builder.ToTable("consent_policy_versions", table =>
        {
            table.HasCheckConstraint("ck_consent_policy_versions_version_number", "version_number > 0");
            table.HasCheckConstraint(
                "ck_consent_policy_versions_approval",
                "status = 'Draft' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND approval_source IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_consent_policy_versions_retired", "status <> 'Retired' OR retired_at IS NOT NULL");
        });
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.DocumentReferenceAr).HasMaxLength(ConsentPolicyVersion.MaxDocumentReferenceLength).IsRequired();
        builder.Property(policy => policy.DocumentReferenceEn).HasMaxLength(ConsentPolicyVersion.MaxDocumentReferenceLength).IsRequired();
        builder.Property(policy => policy.ApprovedBy).HasMaxLength(Approval.MaxApprovedByLength);
        builder.Property(policy => policy.ApprovalSource).HasMaxLength(Approval.MaxSourceLength);

        // Lets consent records prove their type matches their policy's type.
        builder.HasAlternateKey(policy => new { policy.Id, policy.ConsentType }).HasName("ak_consent_policy_versions_id_type");

        builder.HasIndex(policy => new { policy.ConsentType, policy.VersionNumber }, "ux_consent_policy_versions_type_version")
            .HasDatabaseName("ux_consent_policy_versions_type_version")
            .IsUnique();
    }
}
