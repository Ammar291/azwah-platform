using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

public readonly record struct ResponseOptionId(Guid Value) : IEntityId<ResponseOptionId>
{
    public static ResponseOptionId New() => new(Guid.CreateVersion7());

    public static ResponseOptionId From(Guid value) => new(value);
}
