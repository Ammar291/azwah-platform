namespace FamilyResilience.Domain.Assignments;

public sealed record AssignmentPlan(
    IReadOnlyList<AssignmentToCreate> Create, IReadOnlyList<ExistingAssignment> Cancel, IReadOnlyList<ExistingAssignment> Keep);
