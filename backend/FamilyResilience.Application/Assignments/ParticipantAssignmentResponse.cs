using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Assignments;

/// <summary>
/// An assignment as the participant sees it: what to answer (instrument, version and module), in which wave, its state
/// and the session in progress, if any. Never why it was assigned. <see cref="InstrumentKind"/> tells the household
/// assessment, whose result the family's next steps and trend come from, from the family-voices modules (D-146).
/// </summary>
public sealed record ParticipantAssignmentResponse(
    Guid Id,
    Guid MemberId,
    string InstrumentCode,
    TextDto InstrumentName,
    int VersionNumber,
    string? SectionCode,
    TextDto? SectionTitle,
    Guid WaveId,
    string WaveCode,
    AssignmentStatus Status,
    DateTimeOffset AssignedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CompletedAt,
    Guid? ActiveSessionId,
    InstrumentKind InstrumentKind);
