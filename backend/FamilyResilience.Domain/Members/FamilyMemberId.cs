using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Members;

public readonly record struct FamilyMemberId(Guid Value) : IEntityId<FamilyMemberId>
{
    public static FamilyMemberId New() => new(Guid.CreateVersion7());

    public static FamilyMemberId From(Guid value) => new(value);
}
