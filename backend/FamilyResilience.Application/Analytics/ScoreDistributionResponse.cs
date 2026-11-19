namespace FamilyResilience.Application.Analytics;

/// <summary>
/// How the complete score results of one approved scoring rule set fall across its tiers, counting only members with
/// active research-use consent. When <c>suppressed</c>, the total is under the minimum group size: it is null and
/// <c>tiers</c> is empty.
/// </summary>
public sealed record ScoreDistributionResponse(
    string InstrumentCode,
    int VersionNumber,
    string? SectionCode,
    string RuleSetCode,
    int RuleSetVersion,
    int? Total,
    bool Suppressed,
    IReadOnlyList<TierCountResponse> Tiers);
