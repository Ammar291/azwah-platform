using FamilyResilience.Domain.Assignments.Eligibility;

namespace FamilyResilience.Application.Assignments;

/// <summary>Staff-only: reason codes and missing facts are never shown to other household members.</summary>
public sealed record EligibilityDecisionResponse(
    Guid MemberId,
    string InstrumentCode,
    string? SectionCode,
    EligibilityOutcome Outcome,
    bool? Eligible,
    Guid RuleSetId,
    string RuleCode,
    int RuleVersion,
    string ReasonCode,
    IReadOnlyList<string> MissingInformation,
    DateTimeOffset EvaluatedAt);
