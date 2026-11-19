using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Assignments;

public sealed record PreparedAssignmentRun(
    Household Household,
    Wave Wave,
    IReadOnlyList<AssignmentTarget> Targets,
    IReadOnlyList<EligibilityDecision> Decisions,
    AssignmentPlan Plan,
    DateTimeOffset EvaluatedAt)
{
    public AssignmentTarget TargetFor(EligibilityDecision decision) =>
        Targets.Single(target => target.RuleSet.Id == decision.RuleSetId);
}
