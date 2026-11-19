using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Members;

/// <summary>What the household tells us about a member. Relationship and employment are optional (not collected).</summary>
public sealed record FamilyMemberDetails(
    YearMonth BirthMonth,
    HouseholdRelationship? RelationshipToHousehold,
    EmploymentStatus? EmploymentStatus,
    bool IsHeadOfHousehold,
    bool IsPrimaryIncomeEarner);
