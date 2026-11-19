using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.SupportResources;

public readonly record struct SupportResourceId(Guid Value) : IEntityId<SupportResourceId>
{
    public static SupportResourceId New() => new(Guid.CreateVersion7());

    public static SupportResourceId From(Guid value) => new(value);
}
