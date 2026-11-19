using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A session's latest result as the approved rule set computed it: total and band code (tier), for the head only
/// (D-103). The model may explain it; it never computes or changes one.
/// </summary>
public sealed record FamilyContextResult(ScoreStatus Status, decimal? TotalScore, string? BandCode);
