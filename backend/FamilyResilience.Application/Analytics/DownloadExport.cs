using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Opens an export's file for the researcher who requested it, while it is completed and unexpired. A file that is
/// already gone (deleted by maintenance a moment earlier) reads as expired. Every download is audited once the file is
/// open and before it is handed over.
/// </summary>
public sealed class DownloadExport(
    ICurrentUser currentUser, IExportJobRepository jobs, IExportFileStore store, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<ExportDownload>> HandleAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var loaded = await ExportJobs.RequestedByCallerAsync(jobs, currentUser, exportId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<ExportDownload>(loaded.Error!);
        }

        var job = loaded.Value;
        if (!job.IsDownloadable(timeProvider.GetUtcNow()))
        {
            return Result.Failure<ExportDownload>(job.Status is ExportStatus.Completed or ExportStatus.Expired
                ? AnalyticsErrors.ExportExpired
                : AnalyticsErrors.ExportNotReady);
        }

        if (!store.IsConfigured)
        {
            return Result.Failure<ExportDownload>(AnalyticsErrors.ExportStorageNotConfigured);
        }

        var content = await store.OpenReadAsync(job.StorageKey, cancellationToken);
        if (content is null)
        {
            return Result.Failure<ExportDownload>(AnalyticsErrors.ExportExpired);
        }

        Result recorded;
        try
        {
            recorded = await auditLog.RecordAsync(
                new AuditRecord("analytics.export.downloaded", ExportJobs.ResourceType, exportId.ToString(), AuditOutcome.Succeeded), cancellationToken);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }

        if (recorded.IsFailure)
        {
            await content.DisposeAsync();
            return Result.Failure<ExportDownload>(recorded.Error!);
        }

        return Result.Success(new ExportDownload(content, $"azwah-export-{exportId}.csv"));
    }
}
