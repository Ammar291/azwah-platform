using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Consent;

public readonly record struct ConsentRecordId(Guid Value) : IEntityId<ConsentRecordId>
{
    public static ConsentRecordId New() => new(Guid.CreateVersion7());

    public static ConsentRecordId From(Guid value) => new(value);
}
