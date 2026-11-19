namespace FamilyResilience.Application.Scoring;

/// <summary>
/// One check-in on the trend chart: the total and tier its latest result recorded. Points with the same
/// <see cref="Segment"/> were scored by the same rule set, one after another, and are joined by a line; a new segment
/// starts when the rule set changes.
/// </summary>
public sealed record ResilienceTrendPointResponse(
    Guid SessionId,
    string WaveCode,
    DateTimeOffset SubmittedAt,
    decimal TotalScore,
    string? TierCode,
    int Segment);
