using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Analytics;

public readonly record struct ExportJobId(Guid Value) : IEntityId<ExportJobId>
{
    public static ExportJobId New() => new(Guid.CreateVersion7());

    public static ExportJobId From(Guid value) => new(value);
}
