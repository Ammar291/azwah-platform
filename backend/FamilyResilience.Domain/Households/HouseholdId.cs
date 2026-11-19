using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Households;

public readonly record struct HouseholdId(Guid Value) : IEntityId<HouseholdId>
{
    public static HouseholdId New() => new(Guid.CreateVersion7());

    public static HouseholdId From(Guid value) => new(value);
}
