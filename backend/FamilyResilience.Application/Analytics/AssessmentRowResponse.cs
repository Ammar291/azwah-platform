using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// One assignment's progress for program monitoring: what was assigned, to which household, in which wave, and whether
/// it was answered and scored. Never the answers or the score.
/// </summary>
public sealed record AssessmentRowResponse(
    Guid AssignmentId,
    Guid HouseholdId,
    string InstrumentCode,
    int VersionNumber,
    string? SectionCode,
    string WaveCode,
    AssignmentStatus AssignmentStatus,
    DateTimeOffset AssignedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CompletedAt,
    SessionStatus? SessionStatus,
    DateTimeOffset? SubmittedAt,
    bool Scored);
