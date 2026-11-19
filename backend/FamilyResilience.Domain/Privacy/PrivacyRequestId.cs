using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Privacy;

public readonly record struct PrivacyRequestId(Guid Value) : IEntityId<PrivacyRequestId>
{
    public static PrivacyRequestId New() => new(Guid.CreateVersion7());

    public static PrivacyRequestId From(Guid value) => new(value);
}
