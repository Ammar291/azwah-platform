using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Assignments;

public readonly record struct AssessmentAssignmentId(Guid Value) : IEntityId<AssessmentAssignmentId>
{
    public static AssessmentAssignmentId New() => new(Guid.CreateVersion7());

    public static AssessmentAssignmentId From(Guid value) => new(value);
}
