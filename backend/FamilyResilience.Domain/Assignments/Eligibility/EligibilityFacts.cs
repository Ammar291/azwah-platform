using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>
/// What the rules may know about one member, taken from the household roster on one date. <paramref name="Age"/> is
/// what rule thresholds compare (completed years, the birthday counted as the first of the birth month);
/// <paramref name="IsCertainlyAdult"/> is what the adults-only platform guard reads, true only once the member is 18
/// whatever their day of birth.
/// </summary>
public sealed record EligibilityFacts(
    FamilyMemberId MemberId,
    bool IsActive,
    int Age,
    bool IsCertainlyAdult,
    EmploymentStatus? EmploymentStatus,
    bool IsHeadOfHousehold,
    bool IsPrimaryIncomeEarner,
    IReadOnlyList<int> ActiveHouseholdMemberAges)
{
    public static EligibilityFacts From(Household household, FamilyMember member, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(member);

        return new EligibilityFacts(
            member.Id,
            member.IsActive,
            member.AgeOn(asOf),
            member.IsCertainlyAdultOn(asOf),
            member.EmploymentStatus,
            member.IsHeadOfHousehold,
            member.IsPrimaryIncomeEarner,
            household.Members.Where(other => other.IsActive).Select(other => other.AgeOn(asOf)).ToList());
    }
}
