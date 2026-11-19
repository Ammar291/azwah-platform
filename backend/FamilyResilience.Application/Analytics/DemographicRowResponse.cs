namespace FamilyResilience.Application.Analytics;

/// <summary>Assignments (not cancelled) and completions for one group. When <c>suppressed</c>, the counts and rate are null.</summary>
public sealed record DemographicRowResponse(string Group, int? Total, int? Completed, decimal? CompletionRate, bool Suppressed);
