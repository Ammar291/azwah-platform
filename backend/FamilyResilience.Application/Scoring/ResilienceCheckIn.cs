using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// One submitted household-assessment check-in and its latest score result: its status, total, tier and the rule set
/// that recorded it, all null while no approved rule set has scored it.
/// </summary>
public sealed record ResilienceCheckIn(
    Guid SessionId,
    string WaveCode,
    DateTimeOffset SubmittedAt,
    ScoreStatus? ResultStatus,
    decimal? TotalScore,
    string? TierCode,
    RuleSetId? RuleSetId);
