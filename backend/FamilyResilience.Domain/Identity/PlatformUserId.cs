using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Identity;

public readonly record struct PlatformUserId(Guid Value) : IEntityId<PlatformUserId>
{
    public static PlatformUserId New() => new(Guid.CreateVersion7());

    public static PlatformUserId From(Guid value) => new(value);
}
