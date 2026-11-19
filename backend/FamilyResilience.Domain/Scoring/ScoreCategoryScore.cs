using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Scoring;

/// <summary>One category's score inside a result. Never changed.</summary>
public sealed class ScoreCategoryScore : Entity<ScoreCategoryScoreId>
{
    private ScoreCategoryScore(ScoreCategoryScoreId id)
        : base(id)
    {
    }

    public ScoreResultId ScoreResultId { get; private set; }

    public string CategoryCode { get; private set; } = null!;

    public decimal Score { get; private set; }

    public int ItemCount { get; private set; }

    internal static ScoreCategoryScore Create(ScoreResultId resultId, CategoryScore score) =>
        new(ScoreCategoryScoreId.New())
        {
            ScoreResultId = resultId,
            CategoryCode = score.CategoryCode,
            Score = score.Score,
            ItemCount = score.ItemCount,
        };
}
