namespace FamilyResilience.Application.Assistant;

/// <summary>A model's summary of a report, written ahead of the score. The server keeps no copy.</summary>
public sealed record ScoreReportSummaryResponse(string Language, string Summary, string ModelName, DateTimeOffset GeneratedAt);
