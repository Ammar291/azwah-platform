using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One member the caller may act for. <see cref="Label"/> (<c>member_1</c>, <c>member_2</c>, … in roster order) names
/// the member everywhere in the context; it is not stored anywhere and is not an ID.
/// </summary>
public sealed record FamilyContextMember(
    string Label,
    HouseholdRelationship? Relationship,
    int Age,
    EmploymentStatus? EmploymentStatus,
    bool IsHeadOfHousehold,
    bool IsPrimaryIncomeEarner,
    bool HasAccount,
    bool IsCaller);
