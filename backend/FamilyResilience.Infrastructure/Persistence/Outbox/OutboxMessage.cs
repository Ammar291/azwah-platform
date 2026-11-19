namespace FamilyResilience.Infrastructure.Persistence.Outbox;

/// <summary>
/// A domain event waiting to be published, written in the same transaction as the change that raised it. Payloads
/// hold identifiers, enum values and times only. A dispatcher arrives with the first consumer
/// (docs/database-conventions.md).
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxTypeLength = 100;
    public const int MaxErrorCodeLength = 100;

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int RetryCount { get; private set; }

    public OutboxMessageStatus Status { get; private set; }

    /// <summary>A short error code from the last failed attempt; never an exception message.</summary>
    public string? LastErrorCode { get; private set; }

    /// <summary>When a failed message may be tried again; null when it has not failed.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public static OutboxMessage Create(string type, string payload, DateTimeOffset occurredAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Payload = payload,
            OccurredAt = occurredAt,
            Status = OutboxMessageStatus.Pending,
        };

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = OutboxMessageStatus.Processed;
        ProcessedAt = now;
        NextAttemptAt = null;
    }

    /// <summary>
    /// Records a failed attempt with a short code. The message is retried after an exponential backoff (2, 4, 8 … seconds,
    /// at most an hour) and marked Failed once it has used <paramref name="maxAttempts"/> attempts.
    /// </summary>
    public void RecordFailure(string errorCode, int maxAttempts, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        RetryCount++;
        LastErrorCode = errorCode.Length <= MaxErrorCodeLength ? errorCode : errorCode[..MaxErrorCodeLength];
        if (RetryCount >= maxAttempts)
        {
            Status = OutboxMessageStatus.Failed;
            NextAttemptAt = null;
            return;
        }

        NextAttemptAt = now + Backoff(RetryCount);
    }

    public static TimeSpan Backoff(int failedAttempts) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, failedAttempts), TimeSpan.FromHours(1).TotalSeconds));
}
