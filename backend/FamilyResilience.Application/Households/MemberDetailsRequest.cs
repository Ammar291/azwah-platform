using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

/// <summary>A member added to an existing household (<c>POST …/members</c>).</summary>
public sealed record MemberDetailsRequest(
    int BirthYear,
    int BirthMonth,
    HouseholdRelationship? RelationshipToHousehold,
    EmploymentStatus? EmploymentStatus,
    bool? IsHeadOfHousehold,
    bool? IsPrimaryIncomeEarner) : IMemberDetailsRequest;
