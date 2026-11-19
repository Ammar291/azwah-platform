using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Consent;

public readonly record struct ConsentPolicyVersionId(Guid Value) : IEntityId<ConsentPolicyVersionId>
{
    public static ConsentPolicyVersionId New() => new(Guid.CreateVersion7());

    public static ConsentPolicyVersionId From(Guid value) => new(value);
}
