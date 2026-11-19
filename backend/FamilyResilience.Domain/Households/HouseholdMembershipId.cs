using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Households;

public readonly record struct HouseholdMembershipId(Guid Value) : IEntityId<HouseholdMembershipId>
{
    public static HouseholdMembershipId New() => new(Guid.CreateVersion7());

    public static HouseholdMembershipId From(Guid value) => new(value);
}
