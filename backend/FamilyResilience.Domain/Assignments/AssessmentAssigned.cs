using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Assignments;

[DomainEventType("assignments.assessment_assigned.v1")]
public sealed record AssessmentAssigned(
    AssessmentAssignmentId AssessmentAssignmentId,
    FamilyMemberId FamilyMemberId,
    InstrumentVersionId InstrumentVersionId,
    WaveId WaveId,
    DateTimeOffset OccurredAt) : IDomainEvent;
