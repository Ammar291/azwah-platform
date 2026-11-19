using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Assignments;

public sealed record AssignmentSummaryResponse(
    Guid AssignmentId,
    Guid MemberId,
    string InstrumentCode,
    string? SectionCode,
    Guid InstrumentVersionId,
    Guid EligibilityRuleSetId,
    AssignmentReason Reason,
    AssignmentStatus Status);
