using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Queues a research export for the signed-in researcher. The dataset and columns must be in the catalog's allowlists,
/// storage must be configured, and the researcher must be within <see cref="ExportQuotas"/>. The job, its idempotency
/// key, its audit event and the outbox message that starts the work commit together; a retry with the same key returns
/// the same job, even when the quota is full by then. The file is written later by <see cref="ProcessExport"/>.
/// Two requests at the same moment can both pass the quota check; the <c>exports</c> rate limit bounds that.
/// </summary>
public sealed class CreateExport(
    ICurrentUser currentUser,
    IExportJobRepository jobs,
    IWaveRepository waves,
    IExportFileStore store,
    IdempotencyGuard idempotency,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ExportQuotas quotas,
    TimeProvider timeProvider)
{
    public const string Operation = "analytics.export.create";

    public async Task<Result<ExportResponse>> HandleAsync(CreateExportRequest request, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Columns);

        if (!store.IsConfigured)
        {
            return Result.Failure<ExportResponse>(AnalyticsErrors.ExportStorageNotConfigured);
        }

        var dataset = ExportCatalog.ParseDataset(request.Dataset);
        if (dataset.IsFailure)
        {
            return Result.Failure<ExportResponse>(dataset.Error!);
        }

        var columns = request.Columns;
        var allowed = ExportCatalog.CheckColumns(dataset.Value, columns);
        if (allowed.IsFailure)
        {
            return Result.Failure<ExportResponse>(allowed.Error!);
        }

        var waveId = request.Filters?.WaveId;
        var instrumentCode = request.Filters?.InstrumentCode;
        var requestHash = RequestHash.Of(
            Operation, request.Dataset!, string.Join(',', columns), waveId?.ToString() ?? string.Empty, instrumentCode ?? string.Empty);
        var earlier = await idempotency.FindAsync(idempotencyKey, Operation, requestHash, cancellationToken);
        if (earlier.IsFailure)
        {
            return Result.Failure<ExportResponse>(earlier.Error!);
        }

        var now = timeProvider.GetUtcNow();
        if (earlier.Value is { } earlierId)
        {
            var replayed = await jobs.GetAsync(ExportJobId.From(earlierId), cancellationToken)
                ?? throw new InvalidOperationException("An idempotency key must point at a saved export.");
            return Result.Success(replayed.ToResponse(now));
        }

        if (waveId is { } wave && await waves.GetAsync(WaveId.From(wave), cancellationToken) is null)
        {
            return Result.Failure<ExportResponse>(AnalyticsErrors.ExportFilterInvalid);
        }

        var requestedBy = currentUser.SubjectId
            ?? throw new InvalidOperationException("An export needs a signed-in caller.");
        if (await jobs.CountActiveRequestedByAsync(requestedBy, cancellationToken) >= quotas.MaxActivePerRequester)
        {
            return Result.Failure<ExportResponse>(AnalyticsErrors.ExportLimitReached);
        }

        if (await jobs.CountRequestedSinceAsync(requestedBy, now.AddDays(-1), cancellationToken) >= quotas.MaxPerRequesterPerDay)
        {
            return Result.Failure<ExportResponse>(AnalyticsErrors.ExportDailyLimitReached);
        }

        var job = ExportJob.Request(
            requestedBy, dataset.Value, columns, waveId is { } id ? WaveId.From(id) : null, instrumentCode, now);
        if (job.IsFailure)
        {
            return Result.Failure<ExportResponse>(job.Error!);
        }

        jobs.Add(job.Value);
        idempotency.Remember(idempotencyKey, Operation, job.Value.Id.Value, requestHash);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "analytics.export.requested", ExportJobs.ResourceType, job.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<ExportResponse>(committed.Error!)
            : Result.Success(job.Value.ToResponse(now));
    }
}
