namespace FamilyResilience.Application.Assistant;

/// <summary>Whether a report can be written ahead for this session, so the app asks only the people it applies to.</summary>
public sealed record ScoreReportEligibilityResponse(bool Eligible);
