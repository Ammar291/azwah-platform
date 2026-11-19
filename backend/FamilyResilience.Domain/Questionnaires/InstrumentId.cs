using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

public readonly record struct InstrumentId(Guid Value) : IEntityId<InstrumentId>
{
    public static InstrumentId New() => new(Guid.CreateVersion7());

    public static InstrumentId From(Guid value) => new(value);
}
