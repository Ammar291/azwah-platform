using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Referrals;

public readonly record struct SupportReferralId(Guid Value) : IEntityId<SupportReferralId>
{
    public static SupportReferralId New() => new(Guid.CreateVersion7());

    public static SupportReferralId From(Guid value) => new(value);
}
