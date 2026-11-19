using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Identity;

public readonly record struct RoleSnapshotId(Guid Value) : IEntityId<RoleSnapshotId>
{
    public static RoleSnapshotId New() => new(Guid.CreateVersion7());

    public static RoleSnapshotId From(Guid value) => new(value);
}
