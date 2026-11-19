using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Abstractions;

/// <summary>
/// Handles the <c>Idempotency-Key</c> of one operation for the signed-in caller. <see cref="FindAsync"/> tells a retry
/// (the resource the key already produced) from a new request, and refuses a key reused for something else;
/// <see cref="Remember"/> stages the key so it commits with the change it describes.
/// </summary>
public sealed class IdempotencyGuard(IIdempotencyStore store, ICurrentUser currentUser, TimeProvider timeProvider)
{
    /// <summary>The resource an earlier identical request produced, null for a new request, or a key-reuse error.</summary>
    public async Task<Result<Guid?>> FindAsync(Guid key, string operation, string requestHash, CancellationToken cancellationToken)
    {
        var earlier = await store.FindAsync(Subject(), key, timeProvider.GetUtcNow(), cancellationToken);
        if (earlier is null)
        {
            return Result.Success<Guid?>(null);
        }

        return earlier.Matches(operation, requestHash)
            ? Result.Success<Guid?>(earlier.ResourceId)
            : Result.Failure<Guid?>(RequestErrors.IdempotencyKeyReused);
    }

    public void Remember(Guid key, string operation, Guid resourceId, string requestHash) =>
        store.Add(IdempotencyRecord.Create(Subject(), key, operation, resourceId, requestHash, timeProvider.GetUtcNow()));

    private string Subject() =>
        currentUser.SubjectId ?? throw new InvalidOperationException("An idempotent request needs a signed-in caller.");
}
