using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

public readonly record struct InstrumentItemId(Guid Value) : IEntityId<InstrumentItemId>
{
    public static InstrumentItemId New() => new(Guid.CreateVersion7());

    public static InstrumentItemId From(Guid value) => new(value);
}
