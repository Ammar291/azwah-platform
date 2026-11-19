using FamilyResilience.Domain.Analytics;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// An export's request and progress. <c>downloadPath</c> is set only while the file can be downloaded; <c>errorCode</c>
/// only when the export failed.
/// </summary>
public sealed record ExportResponse(
    Guid Id,
    string Dataset,
    IReadOnlyList<string> Columns,
    ExportFiltersResponse Filters,
    ExportStatus Status,
    int? RowCount,
    string? ErrorCode,
    DateTimeOffset RequestedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? ExpiresAt,
    string? DownloadPath);
