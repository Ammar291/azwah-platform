using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Households;

/// <summary>
/// Deletes every row about one household inside the caller's transaction, in an order the database guards accept
/// (docs/database-conventions.md, Retention and deletion). Audit events are kept. Only <see cref="EraseHousehold"/>
/// calls it, after its checks.
/// </summary>
public interface IHouseholdErasure
{
    /// <summary>
    /// Locks the household's row until the caller's transaction ends, so two erasures of one household run one after the
    /// other. False when the row no longer exists: another erasure got there first.
    /// </summary>
    Task<bool> LockAsync(HouseholdId householdId, CancellationToken cancellationToken);

    /// <summary>Deletes everything about the household and returns the rows deleted per table.</summary>
    Task<IReadOnlyDictionary<string, int>> EraseAsync(HouseholdId householdId, CancellationToken cancellationToken);
}
