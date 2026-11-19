namespace FamilyResilience.Application.Analytics;

/// <summary>Assignments that were not cancelled, and how many were completed, per questionnaire version and module.</summary>
public sealed record VersionCompletionResponse(
    string InstrumentCode, int VersionNumber, string? SectionCode, int Total, int Completed, decimal? CompletionRate);
