using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

/// <summary>
/// One roster entry. <c>age</c> is computed by the server on today's date (UTC) for display. <c>hasAccount</c> means the
/// member signs in themselves, so only they can answer for themselves; <c>isCaller</c> marks the caller's own entry.
/// </summary>
public sealed record MemberResponse(
    Guid Id,
    int BirthYear,
    int BirthMonth,
    int Age,
    HouseholdRelationship? RelationshipToHousehold,
    EmploymentStatus? EmploymentStatus,
    bool IsHeadOfHousehold,
    bool IsPrimaryIncomeEarner,
    MemberStatus Status,
    bool HasAccount,
    bool IsCaller,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
