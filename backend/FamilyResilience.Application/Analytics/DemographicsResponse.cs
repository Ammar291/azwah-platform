namespace FamilyResilience.Application.Analytics;

/// <summary>Completion by age band (ages taken on <c>ageAsOf</c>) and by employment status, with small cells suppressed.</summary>
public sealed record DemographicsResponse(
    DateOnly AgeAsOf, IReadOnlyList<DemographicRowResponse> ByAgeBand, IReadOnlyList<DemographicRowResponse> ByEmploymentStatus);
