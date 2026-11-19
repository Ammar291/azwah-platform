namespace FamilyResilience.Api.Identity;

public sealed record HouseholdPermissionsResponse(Guid HouseholdId, IReadOnlyList<string> Permissions);
