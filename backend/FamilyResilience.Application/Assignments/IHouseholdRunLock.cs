using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Assignments;

/// <summary>
/// Serialises assignment runs for one household. Call it first inside the run's explicit transaction: it takes a row
/// lock on the household that is held until that transaction ends, so a second run for the same household waits and
/// then plans against what the first one committed. A household that does not exist is not locked.
/// </summary>
public interface IHouseholdRunLock
{
    Task LockAsync(HouseholdId householdId, CancellationToken cancellationToken = default);
}
