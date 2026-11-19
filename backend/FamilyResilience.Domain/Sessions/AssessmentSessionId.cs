using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Sessions;

public readonly record struct AssessmentSessionId(Guid Value) : IEntityId<AssessmentSessionId>
{
    public static AssessmentSessionId New() => new(Guid.CreateVersion7());

    public static AssessmentSessionId From(Guid value) => new(value);
}
