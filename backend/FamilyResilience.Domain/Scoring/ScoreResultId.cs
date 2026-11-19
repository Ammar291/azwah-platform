using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Scoring;

public readonly record struct ScoreResultId(Guid Value) : IEntityId<ScoreResultId>
{
    public static ScoreResultId New() => new(Guid.CreateVersion7());

    public static ScoreResultId From(Guid value) => new(value);
}
