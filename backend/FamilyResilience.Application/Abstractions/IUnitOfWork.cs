using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Abstractions;

/// <summary>
/// The transaction boundary of a use case. One use case is one transaction: either one <see cref="CommitAsync"/>, or
/// several inside one <see cref="ExecuteInTransactionAsync{T}"/>. Audit events and outbox messages staged during the
/// use case are written in the same transaction. After a failed commit, discard the scope.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves every staged change, audit event and outbox message in one database transaction. Returns
    /// <see cref="PersistenceErrors.ConcurrencyConflict"/>, a module error for a known unique rule, or
    /// <see cref="PersistenceErrors.Conflict"/> for expected conflicts; anything else is a bug and throws.
    /// </summary>
    Task<Result> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one explicit transaction. A failed result or an exception rolls back
    /// everything, including changes already saved by <see cref="CommitAsync"/> inside it. Not reentrant.
    /// </summary>
    Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken = default);
}
