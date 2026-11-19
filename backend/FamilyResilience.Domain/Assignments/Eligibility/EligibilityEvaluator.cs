namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>
/// Decides one member against one rule. Platform guards come first: an inactive member, or a member who is not
/// certainly 18 (adults only, D-58), is never eligible whatever the rule says. Only the birth month is stored, so the
/// guard waits until the month after the 18th birthday month; rule thresholds keep the documented convention.
/// </summary>
public static class EligibilityEvaluator
{
    public static EligibilityDecision Evaluate(EligibilityRule rule, EligibilityFacts facts, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(facts);

        if (!facts.IsActive)
        {
            return Decide(rule, facts, EligibilityOutcome.Ineligible, EligibilityReasons.MemberInactive, [], evaluatedAt);
        }

        if (!facts.IsCertainlyAdult)
        {
            return Decide(rule, facts, EligibilityOutcome.Ineligible, EligibilityReasons.MemberUnder18, [], evaluatedAt);
        }

        var result = rule.Specification.Evaluate(facts);
        return result.Truth switch
        {
            Truth.True => Decide(rule, facts, EligibilityOutcome.Eligible, EligibilityReasons.Eligible, [], evaluatedAt),
            Truth.False => Decide(
                rule,
                facts,
                EligibilityOutcome.Ineligible,
                result.ReasonCode ?? throw new InvalidOperationException("A false rule must name its reason."),
                [],
                evaluatedAt),
            _ => Decide(
                rule, facts, EligibilityOutcome.Indeterminate, EligibilityReasons.MissingInformation, result.MissingInformation, evaluatedAt),
        };
    }

    private static EligibilityDecision Decide(
        EligibilityRule rule,
        EligibilityFacts facts,
        EligibilityOutcome outcome,
        string reasonCode,
        IReadOnlyList<string> missingInformation,
        DateTimeOffset evaluatedAt) =>
        new(
            facts.MemberId,
            rule.InstrumentCode,
            rule.SectionCode,
            outcome,
            rule.RuleSetId,
            rule.RuleCode,
            rule.RuleVersion,
            reasonCode,
            missingInformation,
            evaluatedAt);
}
