using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("consent_records", table =>
            table.HasCheckConstraint(
                "ck_consent_records_withdrawn_after_granted", "withdrawn_at IS NULL OR withdrawn_at >= granted_at"));
        builder.HasKey(consent => consent.Id);
        builder.Property(consent => consent.GrantedBy).HasMaxLength(ConsentRecord.MaxActorIdLength);
        builder.Property(consent => consent.WithdrawnBy).HasMaxLength(ConsentRecord.MaxActorIdLength);

        // Lets referrals prove the consent they rely on belongs to the referred member.
        builder.HasAlternateKey(consent => new { consent.Id, consent.FamilyMemberId }).HasName("ak_consent_records_id_member");

        builder.HasOne<FamilyMember>()
            .WithMany()
            .HasForeignKey(consent => new { consent.FamilyMemberId, consent.HouseholdId })
            .HasPrincipalKey(member => new { member.Id, member.HouseholdId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConsentPolicyVersion>()
            .WithMany()
            .HasForeignKey(consent => new { consent.ConsentPolicyVersionId, consent.ConsentType })
            .HasPrincipalKey(policy => new { policy.Id, policy.ConsentType })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(consent => new { consent.FamilyMemberId, consent.ConsentType }, "ux_consent_records_one_active_per_type")
            .HasDatabaseName("ux_consent_records_one_active_per_type")
            .IsUnique()
            .HasFilter("withdrawn_at IS NULL");
    }
}
