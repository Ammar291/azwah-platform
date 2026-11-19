using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class SupportReferralConfiguration : IEntityTypeConfiguration<SupportReferral>
{
    public void Configure(EntityTypeBuilder<SupportReferral> builder)
    {
        builder.ToTable("support_referrals", table =>
        {
            table.HasCheckConstraint(
                "ck_support_referrals_status_shape",
                "(status = 'Open' AND assigned_service_code IS NULL AND resolution_code IS NULL AND resolved_at IS NULL AND resolved_by IS NULL)"
                + " OR (status = 'InProgress' AND assigned_service_code IS NOT NULL AND resolution_code IS NULL AND resolved_at IS NULL AND resolved_by IS NULL)"
                + " OR (status IN ('Resolved', 'Cancelled') AND resolution_code IS NOT NULL AND resolved_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_support_referrals_hand_off_shape",
                "(partner_organisation_id IS NULL AND hand_off_method IS NULL AND handed_off_at IS NULL AND handed_off_by IS NULL)"
                + " OR (partner_organisation_id IS NOT NULL AND hand_off_method IS NOT NULL AND handed_off_at IS NOT NULL"
                + " AND handed_off_by IS NOT NULL AND assigned_service_code IS NOT NULL AND status <> 'Open')");
        });
        builder.HasKey(referral => referral.Id);
        builder.Property(referral => referral.CategoryCode).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();
        builder.Property(referral => referral.AssignedServiceCode).HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.Property(referral => referral.ResolutionCode).HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.Property(referral => referral.ResolvedBy).HasMaxLength(SupportReferral.MaxActorIdLength);
        builder.Property(referral => referral.HandedOffBy).HasMaxLength(SupportReferral.MaxActorIdLength);
        builder.HasOne<PartnerOrganisation>()
            .WithMany()
            .HasForeignKey(referral => referral.PartnerOrganisationId)
            .HasConstraintName("fk_support_referrals_partner")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(referral => referral.PartnerOrganisationId, "ix_support_referrals_partner")
            .HasDatabaseName("ix_support_referrals_partner");

        // Composite keys pin every copy: the session's member and version, the rule set's version and the consent's
        // member. Names are explicit because the generated ones pass PostgreSQL's 63-character limit and would collide.
        builder.HasOne<AssessmentSession>()
            .WithMany()
            .HasForeignKey(referral => new { referral.AssessmentSessionId, referral.FamilyMemberId })
            .HasPrincipalKey(session => new { session.Id, session.FamilyMemberId })
            .HasConstraintName("fk_support_referrals_session_member")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AssessmentSession>()
            .WithMany()
            .HasForeignKey(referral => new { referral.AssessmentSessionId, referral.InstrumentVersionId })
            .HasPrincipalKey(session => new { session.Id, session.InstrumentVersionId })
            .HasConstraintName("fk_support_referrals_session_version")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RuleSet>()
            .WithMany()
            .HasForeignKey(referral => new { referral.ReferralRuleSetId, referral.InstrumentVersionId })
            .HasPrincipalKey(ruleSet => new { ruleSet.Id, ruleSet.InstrumentVersionId })
            .HasConstraintName("fk_support_referrals_rule_set_version")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConsentRecord>()
            .WithMany()
            .HasForeignKey(referral => new { referral.ConsentRecordId, referral.FamilyMemberId })
            .HasPrincipalKey(consent => new { consent.Id, consent.FamilyMemberId })
            .HasConstraintName("fk_support_referrals_consent_member")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(referral => new { referral.AssessmentSessionId, referral.FamilyMemberId }, "ix_support_referrals_session_member")
            .HasDatabaseName("ix_support_referrals_session_member");
        builder.HasIndex(referral => new { referral.AssessmentSessionId, referral.InstrumentVersionId }, "ix_support_referrals_session_version")
            .HasDatabaseName("ix_support_referrals_session_version");
        builder.HasIndex(referral => new { referral.ReferralRuleSetId, referral.InstrumentVersionId }, "ix_support_referrals_rule_set_version")
            .HasDatabaseName("ix_support_referrals_rule_set_version");
        builder.HasIndex(referral => new { referral.ConsentRecordId, referral.FamilyMemberId }, "ix_support_referrals_consent_member")
            .HasDatabaseName("ix_support_referrals_consent_member");
        builder.HasIndex(referral => new { referral.AssessmentSessionId, referral.CategoryCode }, "ux_support_referrals_live")
            .HasDatabaseName("ux_support_referrals_live")
            .IsUnique()
            .HasFilter("status <> 'Cancelled'");
        builder.HasIndex(referral => new { referral.Status, referral.CreatedAt }, "ix_support_referrals_status_created")
            .HasDatabaseName("ix_support_referrals_status_created");
    }
}
