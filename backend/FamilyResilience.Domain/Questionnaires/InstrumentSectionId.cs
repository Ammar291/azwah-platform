using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

public readonly record struct InstrumentSectionId(Guid Value) : IEntityId<InstrumentSectionId>
{
    public static InstrumentSectionId New() => new(Guid.CreateVersion7());

    public static InstrumentSectionId From(Guid value) => new(value);
}
