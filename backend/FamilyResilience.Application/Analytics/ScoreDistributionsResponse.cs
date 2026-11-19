namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Tier distributions of complete scores, for researchers only (D-62): one per approved scoring rule set and scope,
/// counting members with active research-use consent in active households, with small cells suppressed.
/// </summary>
public sealed record ScoreDistributionsResponse(
    DateTimeOffset GeneratedAt, int MinimumGroupSize, Guid? WaveId, IReadOnlyList<ScoreDistributionResponse> Distributions);
