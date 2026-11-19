using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>Loading an export for its requester, and its response shape.</summary>
internal static class ExportJobs
{
    public const string ResourceType = "export_job";

    /// <summary>The export, when the signed-in caller requested it; anyone else is told it does not exist.</summary>
    public static async Task<Result<ExportJob>> RequestedByCallerAsync(
        IExportJobRepository jobs, ICurrentUser currentUser, Guid exportId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(ExportJobId.From(exportId), cancellationToken);
        return job is not null && currentUser.SubjectId is { } subject && string.Equals(job.RequestedBy, subject, StringComparison.Ordinal)
            ? Result.Success(job)
            : Result.Failure<ExportJob>(AnalyticsErrors.ExportNotFound);
    }

    public static ExportResponse ToResponse(this ExportJob job, DateTimeOffset now) =>
        new(
            job.Id.Value,
            ExportCatalog.CodeOf(job.Dataset),
            job.Columns,
            new ExportFiltersResponse(job.WaveId?.Value, job.InstrumentCode),
            job.Status,
            job.RowCount,
            job.ErrorCode,
            job.RequestedAt,
            job.FinishedAt,
            job.ExpiresAt,
            job.IsDownloadable(now) ? $"/api/v1/admin/exports/{job.Id.Value}/file" : null);
}
