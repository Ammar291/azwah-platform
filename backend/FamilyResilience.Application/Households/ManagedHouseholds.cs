using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Households;

internal static class ManagedHouseholds
{
    /// <summary>
    /// The household, tracked for changes, if the caller is its active manager; otherwise "not found", so a caller cannot
    /// tell another family's household from one that does not exist.
    /// </summary>
    public static async Task<Result<Household>> LoadAsync(
        ICallerAccess access, IHouseholdRepository households, Guid householdId, CancellationToken cancellationToken)
    {
        var id = HouseholdId.From(householdId);
        if (!await access.CanManageHouseholdAsync(id, cancellationToken))
        {
            return Result.Failure<Household>(HouseholdErrors.HouseholdNotFound);
        }

        var household = await households.GetAsync(id, cancellationToken);
        return household is null
            ? Result.Failure<Household>(HouseholdErrors.HouseholdNotFound)
            : Result.Success(household);
    }
}
