namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Assignment counts by status. <c>started</c> counts assignments with at least one session, whatever became of it.
/// <c>completionRate</c> is completed over every assignment that was not cancelled, to four decimal places; null when
/// there is none.
/// </summary>
public sealed record AssignmentTotalsResponse(
    int Total, int Started, int Assigned, int InProgress, int Completed, int Expired, int Cancelled, decimal? CompletionRate);
