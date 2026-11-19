using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

public readonly record struct RuleSetId(Guid Value) : IEntityId<RuleSetId>
{
    public static RuleSetId New() => new(Guid.CreateVersion7());

    public static RuleSetId From(Guid value) => new(value);
}
