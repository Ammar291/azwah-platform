namespace FamilyResilience.Application.Analytics;

/// <summary>Completed assignments in the period starting on <c>periodStart</c> (UTC), zero when there were none.</summary>
public sealed record TrendPointResponse(DateOnly PeriodStart, int Completed);
