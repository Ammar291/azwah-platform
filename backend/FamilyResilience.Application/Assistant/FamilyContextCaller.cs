using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Assistant;

/// <summary>Who is asking: their member's label, relationship to the head (none for the head) and role in the household.</summary>
public sealed record FamilyContextCaller(
    string Member, HouseholdRelationship? Relationship, bool IsHeadOfHousehold, bool ManagesHousehold);
