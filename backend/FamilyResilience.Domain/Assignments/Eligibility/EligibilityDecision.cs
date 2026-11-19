using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>
/// One member against one approved eligibility rule set on one date. Missing facts that could change the result give
/// <see cref="EligibilityOutcome.Indeterminate"/>, never a guess. Staff-only: reason codes are not shown to other
/// household members.
/// </summary>
public sealed record EligibilityDecision(
    FamilyMemberId MemberId,
    string InstrumentCode,
    string? SectionCode,
    EligibilityOutcome Outcome,
    RuleSetId RuleSetId,
    string RuleCode,
    int RuleVersion,
    string ReasonCode,
    IReadOnlyList<string> MissingInformation,
    DateTimeOffset EvaluatedAt)
{
    /// <summary>True or false once decided; null when indeterminate.</summary>
    public bool? Eligible => Outcome switch
    {
        EligibilityOutcome.Eligible => true,
        EligibilityOutcome.Ineligible => false,
        _ => null,
    };
}
