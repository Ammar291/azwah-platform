namespace FamilyResilience.Application.Analytics;

/// <summary>
/// How many complete score results of one scoring rule set reached one tier (a query row, never an individual value).
/// <see cref="Members"/> is the distinct members behind those results and <see cref="DistributionMembers"/> the distinct
/// members behind every tier of the same rule set and scope (the same on each of its rows): a member scored in several
/// waves has several results, so these decide small-cell suppression (D-58), not <see cref="Count"/>.
/// </summary>
public sealed record TierCount(
    string InstrumentCode,
    int VersionNumber,
    string? SectionCode,
    string RuleSetCode,
    int RuleSetVersion,
    string TierCode,
    int Count,
    int Members,
    int DistributionMembers);
