using FamilyResilience.Application.Scoring;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A written report on a recorded score. The total, band and scale are the platform's record, and the authorities in
/// <see cref="NextSteps"/> the approved catalogue's; the rest is a model's text, named by <see cref="ModelName"/>, which
/// nothing in the platform acts on. The server keeps no copy.
/// </summary>
public sealed record ScoreReportResponse(
    Guid SessionId,
    string Language,
    decimal TotalScore,
    string TierCode,
    ScoreScaleResponse Scale,
    string Summary,
    IReadOnlyList<ScoreReportPoint> Strengths,
    IReadOnlyList<ScoreReportPoint> Weaknesses,
    IReadOnlyList<ScoreReportNextStep> NextSteps,
    string ModelName,
    DateTimeOffset GeneratedAt);
