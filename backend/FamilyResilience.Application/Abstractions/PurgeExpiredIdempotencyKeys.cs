using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Abstractions;

/// <summary>Deletes idempotency keys past their 24 hours. Run by the maintenance service; returns how many it removed.</summary>
public sealed class PurgeExpiredIdempotencyKeys(IIdempotencyStore store, TimeProvider timeProvider)
{
    public async Task<Result<int>> HandleAsync(CancellationToken cancellationToken) =>
        Result.Success(await store.PurgeExpiredAsync(timeProvider.GetUtcNow(), cancellationToken));
}
