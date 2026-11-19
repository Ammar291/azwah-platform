namespace FamilyResilience.Application.Analytics;

/// <summary>What a research export may contain, read from <see cref="ExportCatalog"/>, so clients never copy the allowlist.</summary>
public sealed record ExportCatalogResponse(IReadOnlyList<ExportDatasetResponse> Datasets, IReadOnlyList<string> Filters);
