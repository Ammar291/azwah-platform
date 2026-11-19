namespace FamilyResilience.Application.Consent;

/// <summary>The current policy of each consent type; at most one per type, so the list is never longer than three.</summary>
public sealed record CurrentConsentPoliciesResponse(IReadOnlyList<ConsentPolicyResponse> Policies);
