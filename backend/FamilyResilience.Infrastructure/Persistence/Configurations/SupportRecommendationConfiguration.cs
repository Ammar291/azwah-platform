using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class SupportRecommendationConfiguration : IEntityTypeConfiguration<SupportRecommendation>
{
    public void Configure(EntityTypeBuilder<SupportRecommendation> builder)
    {
        builder.ToTable("support_recommendations");
        builder.HasKey(recommendation => recommendation.Id);
        builder.Property(recommendation => recommendation.CategoryCode).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();

        // Composite keys pin the copied member and version to the session's and the rule set's. Names are set by hand
        // because the generated ones pass PostgreSQL's 63-character limit.
        builder.HasOne<AssessmentSession>()
            .WithMany()
            .HasForeignKey(recommendation => new { recommendation.AssessmentSessionId, recommendation.FamilyMemberId })
            .HasPrincipalKey(session => new { session.Id, session.FamilyMemberId })
            .HasConstraintName("fk_support_recommendations_session_member")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AssessmentSession>()
            .WithMany()
            .HasForeignKey(recommendation => new { recommendation.AssessmentSessionId, recommendation.InstrumentVersionId })
            .HasPrincipalKey(session => new { session.Id, session.InstrumentVersionId })
            .HasConstraintName("fk_support_recommendations_session_version")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RuleSet>()
            .WithMany()
            .HasForeignKey(recommendation => new { recommendation.ReferralRuleSetId, recommendation.InstrumentVersionId })
            .HasPrincipalKey(ruleSet => new { ruleSet.Id, ruleSet.InstrumentVersionId })
            .HasConstraintName("fk_support_recommendations_rule_set_version")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ScoreResult>()
            .WithMany()
            .HasForeignKey(recommendation => recommendation.ScoreResultId)
            .HasConstraintName("fk_support_recommendations_score_result")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupportReferral>()
            .WithMany()
            .HasForeignKey(recommendation => recommendation.SupportReferralId)
            .HasConstraintName("fk_support_recommendations_referral")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(recommendation => new { recommendation.AssessmentSessionId, recommendation.FamilyMemberId }, "ix_support_recommendations_session_member")
            .HasDatabaseName("ix_support_recommendations_session_member");
        builder.HasIndex(recommendation => new { recommendation.AssessmentSessionId, recommendation.InstrumentVersionId }, "ix_support_recommendations_session_version")
            .HasDatabaseName("ix_support_recommendations_session_version");
        builder.HasIndex(recommendation => new { recommendation.ReferralRuleSetId, recommendation.InstrumentVersionId }, "ix_support_recommendations_rule_set_version")
            .HasDatabaseName("ix_support_recommendations_rule_set_version");
        builder.HasIndex(recommendation => recommendation.SupportReferralId, "ix_support_recommendations_referral")
            .HasDatabaseName("ix_support_recommendations_referral");
        builder.HasIndex(recommendation => new { recommendation.FamilyMemberId, recommendation.CreatedAt }, "ix_support_recommendations_member_created")
            .HasDatabaseName("ix_support_recommendations_member_created");
        builder.HasIndex(recommendation => new { recommendation.ScoreResultId, recommendation.CategoryCode }, "ux_support_recommendations_score_category")
            .HasDatabaseName("ux_support_recommendations_score_category")
            .IsUnique();
    }
}
