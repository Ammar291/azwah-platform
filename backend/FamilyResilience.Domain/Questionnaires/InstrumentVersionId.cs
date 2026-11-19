using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

public readonly record struct InstrumentVersionId(Guid Value) : IEntityId<InstrumentVersionId>
{
    public static InstrumentVersionId New() => new(Guid.CreateVersion7());

    public static InstrumentVersionId From(Guid value) => new(value);
}
