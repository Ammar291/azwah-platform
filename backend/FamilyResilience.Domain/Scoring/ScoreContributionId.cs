using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Scoring;

public readonly record struct ScoreContributionId(Guid Value) : IEntityId<ScoreContributionId>
{
    public static ScoreContributionId New() => new(Guid.CreateVersion7());

    public static ScoreContributionId From(Guid value) => new(value);
}
