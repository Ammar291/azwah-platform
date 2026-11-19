using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Referrals;

public readonly record struct SupportRecommendationId(Guid Value) : IEntityId<SupportRecommendationId>
{
    public static SupportRecommendationId New() => new(Guid.CreateVersion7());

    public static SupportRecommendationId From(Guid value) => new(value);
}
