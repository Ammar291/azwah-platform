namespace FamilyResilience.Application.Abstractions;

public interface IIdempotencyStore
{
    /// <summary>The caller's unexpired record for <paramref name="key"/>, if any.</summary>
    Task<IdempotencyRecord?> FindAsync(string subject, Guid key, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Stages a record; it commits with the change it describes. A second record for the same caller and key is refused.</summary>
    void Add(IdempotencyRecord record);

    /// <summary>Deletes expired records and returns how many.</summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
