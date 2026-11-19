namespace FamilyResilience.Application.Households;

/// <summary>The roster of a new household, the caller included and marked <c>isSelf</c>.</summary>
public sealed record CreateHouseholdRequest(IReadOnlyList<NewHouseholdMemberRequest>? Members);
