namespace FamilyResilience.Api.Identity;

/// <summary>Permission codes from the caller's policies, and per household from their membership.</summary>
public sealed record PermissionsResponse(IReadOnlyList<string> Permissions, IReadOnlyList<HouseholdPermissionsResponse> Households);
