using System.Text.RegularExpressions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Identity;

namespace FamilyResilience.Domain.Analytics;

/// <summary>
/// A researcher's request for a de-identified export, and its progress. The job records only what was asked (dataset,
/// columns, optional wave and instrument filters) and how it went; the rows live in a file whose key comes from the
/// job's ID, never from the caller. A run that crashes leaves the job Running, and it may begin again until it has used
/// <see cref="MaxAttempts"/> attempts.
/// </summary>
public sealed partial class ExportJob : AggregateRoot<ExportJobId>
{
    public const int MaxRequesterLength = 200;
    public const int MaxColumns = 30;
    public const int MaxAttempts = 3;
    public const int MaxErrorCodeLength = 100;

    private readonly List<string> _columns = [];

    private ExportJob(ExportJobId id)
        : base(id)
    {
    }

    /// <summary>The token subject of the researcher who asked; only they can read or download the export.</summary>
    public string RequestedBy { get; private set; } = null!;

    public ExportDataset Dataset { get; private set; }

    public IReadOnlyList<string> Columns => _columns.AsReadOnly();

    public WaveId? WaveId { get; private set; }

    public string? InstrumentCode { get; private set; }

    public ExportStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public int? RowCount { get; private set; }

    /// <summary>A short error code when the job failed; never an exception message.</summary>
    public string? ErrorCode { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the job completed or failed.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Where the file is stored, derived from the ID so no caller-supplied path reaches storage.</summary>
    public string StorageKey => $"exports/{Id.Value}.csv";

    public static Result<ExportJob> Request(
        string requestedBy,
        ExportDataset dataset,
        IReadOnlyList<string> columns,
        WaveId? waveId,
        string? instrumentCode,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(columns);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<ExportJob>(CommonErrors.TimeNotUtc);
        }

        if (string.IsNullOrWhiteSpace(requestedBy) || requestedBy.Length > MaxRequesterLength)
        {
            return Result.Failure<ExportJob>(IdentityErrors.SubjectInvalid);
        }

        if (!Enum.IsDefined(dataset))
        {
            return Result.Failure<ExportJob>(AnalyticsErrors.ExportDatasetInvalid);
        }

        if (columns.Count is 0 or > MaxColumns
            || !columns.All(Codes.IsSnakeCode)
            || columns.Distinct(StringComparer.Ordinal).Count() != columns.Count)
        {
            return Result.Failure<ExportJob>(AnalyticsErrors.ExportColumnInvalid);
        }

        if (instrumentCode is not null && !Codes.IsSnakeCode(instrumentCode))
        {
            return Result.Failure<ExportJob>(AnalyticsErrors.ExportFilterInvalid);
        }

        var job = new ExportJob(ExportJobId.New())
        {
            RequestedBy = requestedBy,
            Dataset = dataset,
            WaveId = waveId,
            InstrumentCode = instrumentCode,
            Status = ExportStatus.Queued,
            RequestedAt = now,
            UpdatedAt = now,
        };
        job._columns.AddRange(columns);
        job.Raise(new ExportRequested(job.Id, now));
        return Result.Success(job);
    }

    /// <summary>Starts an attempt: from Queued, or from Running again after a crash, while attempts remain.</summary>
    public Result Begin(DateTimeOffset now)
    {
        if (Status is not (ExportStatus.Queued or ExportStatus.Running))
        {
            return Result.Failure(AnalyticsErrors.ExportTransitionInvalid);
        }

        if (Attempts >= MaxAttempts)
        {
            return Result.Failure(AnalyticsErrors.ExportAttemptsExhausted);
        }

        Status = ExportStatus.Running;
        Attempts++;
        StartedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Complete(int rowCount, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresAt, now);

        if (Status != ExportStatus.Running)
        {
            return Result.Failure(AnalyticsErrors.ExportTransitionInvalid);
        }

        Status = ExportStatus.Completed;
        RowCount = rowCount;
        FinishedAt = now;
        ExpiresAt = expiresAt;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Fail(string errorCode, DateTimeOffset now)
    {
        if (errorCode is null || errorCode.Length > MaxErrorCodeLength || !ErrorCodeFormat().IsMatch(errorCode))
        {
            throw new ArgumentException("An export failure needs an error code such as analytics.export_too_large.", nameof(errorCode));
        }

        if (Status is not (ExportStatus.Queued or ExportStatus.Running))
        {
            return Result.Failure(AnalyticsErrors.ExportTransitionInvalid);
        }

        Status = ExportStatus.Failed;
        ErrorCode = errorCode;
        FinishedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Marks a completed export expired once its file has been deleted.</summary>
    public Result Expire(DateTimeOffset now)
    {
        if (Status != ExportStatus.Completed)
        {
            return Result.Failure(AnalyticsErrors.ExportTransitionInvalid);
        }

        Status = ExportStatus.Expired;
        UpdatedAt = now;
        return Result.Success();
    }

    public bool IsDownloadable(DateTimeOffset now) => Status == ExportStatus.Completed && ExpiresAt > now;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodeFormat();
}
