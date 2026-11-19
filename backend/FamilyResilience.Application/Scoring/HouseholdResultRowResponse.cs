using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// A scored session's latest result for system administrators: which household, wave and questionnaire, and the total
/// and tier exactly as the rule set recorded them (D-104). Never the answers or the members.
/// </summary>
public sealed record HouseholdResultRowResponse(
    Guid SessionId,
    Guid HouseholdId,
    string HouseholdReference,
    string WaveCode,
    string InstrumentCode,
    int VersionNumber,
    string? SectionCode,
    ScoreStatus State,
    decimal? TotalScore,
    string? TierCode,
    DateTimeOffset ComputedAt);
