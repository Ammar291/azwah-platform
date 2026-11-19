using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// One score result of an approved scoring rule set, for a member with research-use consent, as the export query reads
/// it. The IDs and the birth month stay inside the platform: <see cref="ExportCatalog"/> turns them into pseudonyms and
/// an age band. <see cref="ExcludedCount"/> is how many of the result's item contributions were excluded as
/// non-substantive, so a prorated total can be told from a full one; <see cref="IncompleteReason"/> is the scoring
/// policy's fixed code, set only when the result is incomplete.
/// </summary>
public sealed record ExportScoreRow(
    FamilyMemberId MemberId,
    HouseholdId HouseholdId,
    string InstrumentCode,
    int VersionNumber,
    string? SectionCode,
    string WaveCode,
    string RuleSetCode,
    int RuleSetVersion,
    ScoreStatus Status,
    string? IncompleteReason,
    decimal? TotalScore,
    string? TierCode,
    int ExcludedCount,
    DateTimeOffset ComputedAt,
    int BirthYear,
    int BirthMonth,
    EmploymentStatus? EmploymentStatus);
