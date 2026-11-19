namespace FamilyResilience.Application.Assistant;

/// <summary>The language to write a report in. Everything else the report says comes from the platform's records.</summary>
public sealed record ScoreReportRequest(string? Language);
