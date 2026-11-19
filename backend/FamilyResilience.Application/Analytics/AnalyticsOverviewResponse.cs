namespace FamilyResilience.Application.Analytics;

/// <summary>
/// De-identified aggregates for researchers and program administrators. Every figure is a count or a rate computed in
/// the database; demographic cells below <c>minimumGroupSize</c> are suppressed. No scores: score distributions are a
/// separate, researcher-only read (D-62).
/// </summary>
public sealed record AnalyticsOverviewResponse(
    DateTimeOffset GeneratedAt,
    int MinimumGroupSize,
    Guid? WaveId,
    DateOnly From,
    DateOnly To,
    TrendInterval Interval,
    EnrollmentCounts Enrollment,
    AssignmentTotalsResponse Assignments,
    IReadOnlyList<VersionCompletionResponse> ByVersion,
    IReadOnlyList<TrendPointResponse> CompletionTrend,
    DemographicsResponse Demographics);
