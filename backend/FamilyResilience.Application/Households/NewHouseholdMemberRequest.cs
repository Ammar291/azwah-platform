using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

/// <summary>A member of a new household's roster; exactly one is the caller (<see cref="IsSelf"/>).</summary>
public sealed record NewHouseholdMemberRequest(
    int BirthYear,
    int BirthMonth,
    HouseholdRelationship? RelationshipToHousehold,
    EmploymentStatus? EmploymentStatus,
    bool? IsHeadOfHousehold,
    bool? IsPrimaryIncomeEarner,
    bool? IsSelf) : IMemberDetailsRequest;
