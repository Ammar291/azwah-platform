using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// A recorded score for staff. Per-item contributions are not returned: they carry the raw answers, and an
/// administrative role alone never opens a participant's answers (D-48). They stay stored with the result.
/// </summary>
public sealed record ScoreResultResponse(
    Guid Id,
    Guid SessionId,
    Guid InstrumentVersionId,
    Guid RuleSetId,
    string PolicyCode,
    int AlgorithmVersion,
    ScoreStatus Status,
    string? IncompleteReason,
    decimal? TotalScore,
    string? TierCode,
    DateTimeOffset ComputedAt,
    IReadOnlyList<CategoryScoreResponse> CategoryScores,
    int ContributionCount);
