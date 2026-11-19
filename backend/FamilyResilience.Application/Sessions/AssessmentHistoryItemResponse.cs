using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// One submitted check-in: what was answered, when, and the latest result's status and, for the household's head only
/// (D-103), its tier.
/// </summary>
public sealed record AssessmentHistoryItemResponse(
    Guid SessionId,
    Guid AssignmentId,
    Guid MemberId,
    string InstrumentCode,
    int VersionNumber,
    string? SectionCode,
    string WaveCode,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    ScoreStatus? ScoreStatus,
    string? TierCode);
