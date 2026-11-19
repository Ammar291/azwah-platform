using FamilyResilience.Domain.Assignments.Eligibility;

namespace FamilyResilience.Domain.Assignments;

public sealed record AssignmentToCreate(EligibilityDecision Decision, AssignmentReason Reason);
