using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Scoring;

public readonly record struct ScoreCategoryScoreId(Guid Value) : IEntityId<ScoreCategoryScoreId>
{
    public static ScoreCategoryScoreId New() => new(Guid.CreateVersion7());

    public static ScoreCategoryScoreId From(Guid value) => new(value);
}
