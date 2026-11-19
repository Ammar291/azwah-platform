using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Export housekeeping, run by the maintenance service, one commit per export. Completed exports past their expiry lose
/// their file and become Expired. Queued or running exports that have not moved for
/// <see cref="ExportSettings.StalledAfterHours"/> (their outbox message failed, or the worker stopped) are failed with
/// <c>analytics.export_stalled</c> and any file is deleted, so no research file outlives its job. Returns how many
/// exports it expired or failed.
/// </summary>
public sealed class ExpireExports(
    IExportJobRepository jobs,
    IExportFileStore store,
    ExportSettings settings,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    private const int BatchSize = 100;

    public async Task<Result<int>> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var changed = 0;
        foreach (var job in await jobs.ListExpiredAsync(now, BatchSize, cancellationToken))
        {
            await DeleteFileAsync(job, cancellationToken);
            var marked = job.Expire(now);
            if (marked.IsFailure)
            {
                return Result.Failure<int>(marked.Error!);
            }

            var committed = await AuditedCommit.RunAsync(
                auditLog, unitOfWork, "analytics.export.expired", ExportJobs.ResourceType, job.Id.Value, cancellationToken);
            if (committed.IsFailure)
            {
                return Result.Failure<int>(committed.Error!);
            }

            changed++;
        }

        foreach (var job in await jobs.ListStalledAsync(now.AddHours(-settings.StalledAfterHours), BatchSize, cancellationToken))
        {
            await DeleteFileAsync(job, cancellationToken);
            var failed = job.Fail(AnalyticsErrors.ExportStalled.Code, now);
            if (failed.IsFailure)
            {
                return Result.Failure<int>(failed.Error!);
            }

            var staged = auditLog.Stage(new AuditRecord(
                "analytics.export.failed", ExportJobs.ResourceType, job.Id.Value.ToString(), AuditOutcome.Failed));
            var committed = staged.IsFailure ? staged : await unitOfWork.CommitAsync(cancellationToken);
            if (committed.IsFailure)
            {
                return Result.Failure<int>(committed.Error!);
            }

            changed++;
        }

        return Result.Success(changed);
    }

    private async Task DeleteFileAsync(ExportJob job, CancellationToken cancellationToken)
    {
        if (store.IsConfigured)
        {
            await store.DeleteAsync(job.StorageKey, cancellationToken);
        }
    }
}
