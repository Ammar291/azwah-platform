using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Domain.Scoring;

[DomainEventType("scoring.score_result_recorded.v1")]
public sealed record ScoreResultRecorded(
    ScoreResultId ScoreResultId, AssessmentSessionId AssessmentSessionId, RuleSetId RuleSetId, DateTimeOffset OccurredAt)
    : IDomainEvent;
