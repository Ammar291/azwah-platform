namespace FamilyResilience.Application.Analytics;

/// <summary>A dataset a research export can be made of, and the only columns it may contain.</summary>
public sealed record ExportDatasetResponse(string Code, IReadOnlyList<string> Columns);
