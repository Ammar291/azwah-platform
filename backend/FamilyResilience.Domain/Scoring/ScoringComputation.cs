namespace FamilyResilience.Domain.Scoring;

/// <summary>What a strategy computed. Recorded as a <see cref="ScoreResult"/> under the rule set that asked for it.</summary>
public sealed record ScoringComputation(
    string PolicyCode,
    int AlgorithmVersion,
    ScoreStatus Status,
    string? IncompleteReason,
    decimal? TotalScore,
    string? TierCode,
    IReadOnlyList<ItemContribution> Contributions,
    IReadOnlyList<CategoryScore> CategoryScores);
