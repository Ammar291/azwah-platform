namespace FamilyResilience.Application.Assignments;

public sealed record AssignmentRunResponse(
    Guid HouseholdId,
    Guid WaveId,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<EligibilityDecisionResponse> Decisions,
    IReadOnlyList<AssignmentSummaryResponse> Created,
    IReadOnlyList<AssignmentSummaryResponse> Cancelled,
    IReadOnlyList<AssignmentSummaryResponse> Kept);
