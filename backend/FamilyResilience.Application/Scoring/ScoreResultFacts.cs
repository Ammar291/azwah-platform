using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// What a recorded score result says, as codes and IDs only: no total, answers or contributions.
/// <paramref name="RuleSetCode"/> is the code of the approved scoring rule set that produced the result (for example
/// <c>household_assessment_total</c>), which defines the result's tier codes; it is not the strategy code
/// (<see cref="ScoreResult.ScoringPolicyCode"/>, for example <c>summed_item_values</c>), which many rule sets share.
/// <paramref name="TierCode"/> is null for an incomplete result or a rule set without bands. The member is the session's
/// assignment member, and the household is that member's.
/// </summary>
public sealed record ScoreResultFacts(
    string RuleSetCode,
    ScoreStatus Status,
    string? TierCode,
    AssessmentSessionId SessionId,
    HouseholdId HouseholdId,
    FamilyMemberId MemberId);
