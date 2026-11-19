namespace FamilyResilience.Application.Analytics;

public sealed record AssignmentCountsResponse(int Assigned, int InProgress, int Completed, int Expired, int Cancelled);
