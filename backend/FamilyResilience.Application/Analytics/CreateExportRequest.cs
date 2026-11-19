namespace FamilyResilience.Application.Analytics;

/// <summary>
/// A research export: <c>dataset</c> is <c>assessment_sessions</c> or <c>score_results</c>, <c>columns</c> are 1 to 30
/// column codes from that dataset's allowlist, in the order the file should have them.
/// </summary>
public sealed record CreateExportRequest(string? Dataset, IReadOnlyList<string>? Columns, ExportFiltersRequest? Filters);
