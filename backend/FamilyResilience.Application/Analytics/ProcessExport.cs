using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Writes an export's file. Idempotent: a finished job changes nothing. Each attempt is committed before any row is read,
/// so a crash still counts against <see cref="ExportJob.MaxAttempts"/>; after the last one the job fails. Rows come only
/// from members with active research-use consent, at most <see cref="ExportSettings.MaxRows"/> of them; people become
/// pseudonyms under a key that exists for this run only, and rows are ordered by pseudonym so the file does not reveal
/// enrolment order. The file replaces any partial one in one step, and the job completes once.
/// </summary>
public sealed class ProcessExport(
    IExportJobRepository jobs,
    IExportQueries queries,
    IExportFileStore store,
    ExportSettings settings,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid exportJobId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(ExportJobId.From(exportJobId), cancellationToken);
        if (job is null || job.Status is not (ExportStatus.Queued or ExportStatus.Running))
        {
            return Result.Success();
        }

        if (!store.IsConfigured)
        {
            return await FailAsync(job, AnalyticsErrors.ExportStorageNotConfigured, cancellationToken);
        }

        var begun = job.Begin(timeProvider.GetUtcNow());
        if (begun.Error == AnalyticsErrors.ExportAttemptsExhausted)
        {
            return await FailAsync(job, AnalyticsErrors.ExportAttemptsExhausted, cancellationToken);
        }

        if (begun.IsFailure)
        {
            return begun;
        }

        var started = await unitOfWork.CommitAsync(cancellationToken);
        if (started.IsFailure)
        {
            return started;
        }

        var filter = new ExportFilter(job.WaveId, job.InstrumentCode);
        using var pseudonyms = new Pseudonymizer();
        var rows = job.Dataset == ExportDataset.AssessmentSessions
            ? (await queries.SessionsAsync(filter, settings.MaxRows + 1, cancellationToken))
                .Select(row => (Key: pseudonyms.Member(row.MemberId), Cells: ExportCatalog.Project(row, job.Columns, pseudonyms)))
                .ToList()
            : (await queries.ScoresAsync(filter, settings.MaxRows + 1, cancellationToken))
                .Select(row => (Key: pseudonyms.Member(row.MemberId), Cells: ExportCatalog.Project(row, job.Columns, pseudonyms)))
                .ToList();
        if (rows.Count > settings.MaxRows)
        {
            return await FailAsync(job, AnalyticsErrors.ExportTooLarge, cancellationToken);
        }

        var ordered = rows
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ThenBy(row => string.Join('\u001f', row.Cells), StringComparer.Ordinal)
            .Select(row => row.Cells)
            .ToList();
        await store.WriteAsync(job.StorageKey, (stream, token) => CsvWriter.WriteAsync(stream, job.Columns, ordered, token), cancellationToken);

        var now = timeProvider.GetUtcNow();
        var completed = job.Complete(ordered.Count, now.AddDays(settings.RetentionDays), now);
        var committed = completed.IsFailure
            ? completed
            : await AuditedCommit.RunAsync(
                auditLog, unitOfWork, "analytics.export.completed", ExportJobs.ResourceType, job.Id.Value, cancellationToken);
        if (committed.IsFailure)
        {
            // The job changed meanwhile (maintenance failed it as stalled, say): no completed job owns this file. A retry
            // writes it again if the job is still running.
            await store.DeleteAsync(job.StorageKey, cancellationToken);
        }

        return committed;
    }

    /// <summary>Fails the job and deletes any file an earlier attempt wrote, so a failed export never leaves rows behind.</summary>
    private async Task<Result> FailAsync(ExportJob job, Error reason, CancellationToken cancellationToken)
    {
        if (store.IsConfigured)
        {
            await store.DeleteAsync(job.StorageKey, cancellationToken);
        }

        var failed = job.Fail(reason.Code, timeProvider.GetUtcNow());
        if (failed.IsFailure)
        {
            return failed;
        }

        var staged = auditLog.Stage(new AuditRecord(
            "analytics.export.failed", ExportJobs.ResourceType, job.Id.Value.ToString(), AuditOutcome.Failed));
        return staged.IsFailure ? staged : await unitOfWork.CommitAsync(cancellationToken);
    }
}
