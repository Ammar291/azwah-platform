namespace FamilyResilience.Application.Assignments;

public sealed record EligibilityEvaluationResponse(
    Guid HouseholdId,
    Guid WaveId,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<EligibilityDecisionResponse> Decisions,
    IReadOnlyList<PlannedAssignmentResponse> WouldCreate,
    IReadOnlyList<AssignmentSummaryResponse> WouldCancel,
    IReadOnlyList<AssignmentSummaryResponse> WouldKeep);
