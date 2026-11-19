using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Sessions;

public readonly record struct AssessmentResponseId(Guid Value) : IEntityId<AssessmentResponseId>
{
    public static AssessmentResponseId New() => new(Guid.CreateVersion7());

    public static AssessmentResponseId From(Guid value) => new(value);
}
