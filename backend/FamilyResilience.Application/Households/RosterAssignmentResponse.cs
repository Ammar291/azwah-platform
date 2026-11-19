using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Households;

/// <summary>
/// An assignment the confirmed roster led to. It says who answers what; it never says why (eligibility reason codes stay
/// with staff).
/// </summary>
public sealed record RosterAssignmentResponse(
    Guid AssignmentId, Guid MemberId, string InstrumentCode, string? SectionCode, AssignmentStatus Status);
