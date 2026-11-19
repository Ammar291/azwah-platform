using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Assignments.Eligibility;

namespace FamilyResilience.Application.Assignments;

internal static class AssignmentResponses
{
    public static EligibilityDecisionResponse ToResponse(this EligibilityDecision decision) =>
        new(
            decision.MemberId.Value,
            decision.InstrumentCode,
            decision.SectionCode,
            decision.Outcome,
            decision.Eligible,
            decision.RuleSetId.Value,
            decision.RuleCode,
            decision.RuleVersion,
            decision.ReasonCode,
            decision.MissingInformation,
            decision.EvaluatedAt);

    public static AssignmentSummaryResponse Summary(AssessmentAssignment assignment, string instrumentCode, string? sectionCode) =>
        new(
            assignment.Id.Value,
            assignment.FamilyMemberId.Value,
            instrumentCode,
            sectionCode,
            assignment.InstrumentVersionId.Value,
            assignment.EligibilityRuleSetId.Value,
            assignment.Reason,
            assignment.Status);

    public static AssignmentSummaryResponse ToSummary(this ExistingAssignment existing) =>
        Summary(existing.Assignment, existing.InstrumentCode, existing.SectionCode);
}
