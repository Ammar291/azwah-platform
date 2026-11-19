using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Assignments;

public sealed record PlannedAssignmentResponse(Guid MemberId, string InstrumentCode, string? SectionCode, Guid RuleSetId, AssignmentReason Reason);
