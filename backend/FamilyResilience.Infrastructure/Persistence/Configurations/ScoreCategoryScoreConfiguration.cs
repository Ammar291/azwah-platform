using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyResilience.Infrastructure.Persistence.Configurations;

public sealed class ScoreCategoryScoreConfiguration : IEntityTypeConfiguration<ScoreCategoryScore>
{
    public void Configure(EntityTypeBuilder<ScoreCategoryScore> builder)
    {
        builder.ToTable("score_category_scores", table =>
            table.HasCheckConstraint("ck_score_category_scores_item_count", "item_count > 0"));
        builder.HasKey(category => category.Id);
        builder.Property(category => category.CategoryCode).HasMaxLength(Codes.MaxSnakeCodeLength).IsRequired();
        builder.Property(category => category.Score).HasPrecision(9, 3);
        builder.HasIndex(category => new { category.ScoreResultId, category.CategoryCode }, "ux_score_category_scores_result_category")
            .HasDatabaseName("ux_score_category_scores_result_category")
            .IsUnique();
    }
}
