using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

internal static class ScoringResponses
{
    public static ScoreResultResponse ToResponse(this ScoreResult result) =>
        new(
            result.Id.Value,
            result.AssessmentSessionId.Value,
            result.InstrumentVersionId.Value,
            result.RuleSetId.Value,
            result.ScoringPolicyCode,
            result.ScoringAlgorithmVersion,
            result.Status,
            result.IncompleteReason,
            result.TotalScore,
            result.TierCode,
            result.ComputedAt,
            result.CategoryScores.ToResponses(),
            result.Contributions.Count);

    public static IReadOnlyList<CategoryScoreResponse> ToResponses(this IEnumerable<ScoreCategoryScore> categories) =>
        categories
            .OrderBy(category => category.CategoryCode, StringComparer.Ordinal)
            .Select(category => new CategoryScoreResponse(category.CategoryCode, category.Score, category.ItemCount))
            .ToList();
}
