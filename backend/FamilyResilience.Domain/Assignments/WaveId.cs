using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Assignments;

public readonly record struct WaveId(Guid Value) : IEntityId<WaveId>
{
    public static WaveId New() => new(Guid.CreateVersion7());

    public static WaveId From(Guid value) => new(value);
}
