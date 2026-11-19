using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class ScoreResultConfiguration : IEntityTypeConfiguration<ScoreResult>
{
    public void Configure(EntityTypeBuilder<ScoreResult> builder)
    {
        builder.ToTable("score_results", table =>
        {
            table.HasCheckConstraint("ck_score_results_algorithm_version", "scoring_algorithm_version > 0");
            table.HasCheckConstraint(
                "ck_score_results_status_shape",
                "(status = 'Complete' AND incomplete_reason IS NULL)"
                + " OR (status = 'Incomplete' AND incomplete_reason IS NOT NULL AND total_score IS NULL AND tier_code IS NULL)");
            table.HasCheckConstraint("ck_score_results_tier_needs_total", "tier_code IS NULL OR total_score IS NOT NULL");
        });
        builder.HasKey(result => result.Id);
        builder.Property(result => result.ScoringPolicyCode).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();
        builder.Property(result => result.IncompleteReason).HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.Property(result => result.TierCode).HasMaxLength(Codes.MaxSnakeCodeLength);
        builder.Property(result => result.TotalScore).HasPrecision(9, 3);

        // Contributions point at (id, instrument_version_id), so they cannot name an item of another version.
        builder.HasAlternateKey(result => new { result.Id, result.InstrumentVersionId }).HasName("ak_score_results_id_version");

        builder.HasOne<AssessmentSession>()
            .WithMany()
            .HasForeignKey(result => new { result.AssessmentSessionId, result.InstrumentVersionId })
            .HasPrincipalKey(session => new { session.Id, session.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RuleSet>()
            .WithMany()
            .HasForeignKey(result => new { result.RuleSetId, result.InstrumentVersionId })
            .HasPrincipalKey(ruleSet => new { ruleSet.Id, ruleSet.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(result => result.Contributions)
            .WithOne()
            .HasForeignKey(contribution => new { contribution.ScoreResultId, contribution.InstrumentVersionId })
            .HasPrincipalKey(result => new { result.Id, result.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(result => result.Contributions).HasField("_contributions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(result => result.CategoryScores)
            .WithOne()
            .HasForeignKey(category => category.ScoreResultId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(result => result.CategoryScores).HasField("_categoryScores").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(result => new { result.AssessmentSessionId, result.RuleSetId }, "ux_score_results_session_rule_set")
            .HasDatabaseName("ux_score_results_session_rule_set")
            .IsUnique();
    }
}
