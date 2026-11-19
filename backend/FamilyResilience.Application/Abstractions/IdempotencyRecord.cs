namespace FamilyResilience.Application.Abstractions;

/// <summary>
/// A successful request remembered by its <c>Idempotency-Key</c> for 24 hours. It keeps no response body: a replay
/// re-reads the resource it names, so nothing sensitive is stored here. Written in the same transaction as the change.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int MaxSubjectLength = 200;
    public const int MaxOperationLength = 100;
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private IdempotencyRecord()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The token subject of the caller; keys are scoped to their caller.</summary>
    public string Subject { get; private set; } = null!;

    public Guid Key { get; private set; }

    /// <summary>A dotted operation code, for example <c>sessions.submit</c>.</summary>
    public string Operation { get; private set; } = null!;

    public Guid ResourceId { get; private set; }

    /// <summary>SHA-256 of the canonical request, in lowercase hexadecimal.</summary>
    public string RequestHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public static IdempotencyRecord Create(
        string subject, Guid key, string operation, Guid resourceId, string requestHash, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Subject = subject,
            Key = key,
            Operation = operation,
            ResourceId = resourceId,
            RequestHash = requestHash,
            CreatedAt = now,
            ExpiresAt = now + Retention,
        };

    /// <summary>Whether a retry with the same key asks for exactly this operation on the same request.</summary>
    public bool Matches(string operation, string requestHash) =>
        string.Equals(Operation, operation, StringComparison.Ordinal) && string.Equals(RequestHash, requestHash, StringComparison.Ordinal);
}
