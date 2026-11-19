using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class ScoreContributionConfiguration : IEntityTypeConfiguration<ScoreContribution>
{
    public void Configure(EntityTypeBuilder<ScoreContribution> builder)
    {
        builder.ToTable("score_contributions", table =>
            table.HasCheckConstraint(
                "ck_score_contributions_excluded",
                "(is_excluded AND calculated_value IS NULL) OR (NOT is_excluded AND calculated_value IS NOT NULL)"));
        builder.HasKey(contribution => contribution.Id);
        builder.Property(contribution => contribution.RawValue).HasConversion<short>();
        builder.Property(contribution => contribution.CalculatedValue).HasPrecision(9, 3);
        builder.Property(contribution => contribution.ScoringCategory).HasMaxLength(Codes.MaxSnakeCodeLength);

        // The item must belong to the result's version.
        builder.HasOne<InstrumentItem>()
            .WithMany()
            .HasForeignKey(contribution => new { contribution.InstrumentItemId, contribution.InstrumentVersionId })
            .HasPrincipalKey(item => new { item.Id, item.InstrumentVersionId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(contribution => new { contribution.ScoreResultId, contribution.InstrumentItemId }, "ux_score_contributions_result_item")
            .HasDatabaseName("ux_score_contributions_result_item")
            .IsUnique();
    }
}
