using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>A session and its progress: how many in-scope items are answered, and how many are required.</summary>
public sealed record SessionResponse(
    Guid Id,
    Guid AssignmentId,
    Guid MemberId,
    Guid InstrumentVersionId,
    int VersionNumber,
    string? SectionCode,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ScoredAt,
    int AnsweredCount,
    int RequiredCount);
