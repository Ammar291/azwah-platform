namespace FamilyResilience.Application.Identity;

/// <summary>
/// Who the API thinks the caller is: the token subject, the token's roles, their household, if any, and when they were
/// registered as a platform user (null until they confirm their details or create a household).
/// </summary>
public sealed record MeResponse(string Subject, IReadOnlyList<string> Roles, MyHouseholdResponse? Household, DateTimeOffset? RegisteredAt);
