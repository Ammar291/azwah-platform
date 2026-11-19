using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Sessions;

[DomainEventType("sessions.assessment_session_submitted.v1")]
public sealed record AssessmentSessionSubmitted(
    AssessmentSessionId AssessmentSessionId, AssessmentAssignmentId AssessmentAssignmentId, DateTimeOffset OccurredAt)
    : IDomainEvent;
