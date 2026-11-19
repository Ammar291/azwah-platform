using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Households;

public sealed class GetHousehold(ICallerAccess access, IHouseholdRepository households)
{
    public async Task<Result<HouseholdResponse>> HandleAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var household = await ManagedHouseholds.LoadAsync(access, households, householdId, cancellationToken);
        return household.IsFailure
            ? Result.Failure<HouseholdResponse>(household.Error!)
            : Result.Success(household.Value.ToResponse());
    }
}
