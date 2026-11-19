using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

internal static class HouseholdMappings
{
    public static HouseholdResponse ToResponse(this Household household) =>
        new(
            household.Id.Value,
            household.Reference.Value,
            household.Status,
            household.CreatedAt,
            household.UpdatedAt,
            household.RosterConfirmedAt,
            household.Members.Count(member => member.IsActive));

    public static MemberResponse ToResponse(this FamilyMember member, Household household, PlatformUserId? caller, DateOnly today) =>
        new(
            member.Id.Value,
            member.BirthMonth.Year,
            member.BirthMonth.Month,
            member.AgeOn(today),
            member.RelationshipToHousehold,
            member.EmploymentStatus,
            member.IsHeadOfHousehold,
            member.IsPrimaryIncomeEarner,
            member.Status,
            household.ActiveMembershipFor(member.Id) is not null,
            caller is { } user && household.FindActiveMembership(user)?.FamilyMemberId == member.Id,
            member.CreatedAt,
            member.UpdatedAt);

    /// <summary>Members in the order they joined the roster, so pages are stable.</summary>
    public static IEnumerable<FamilyMember> InRosterOrder(this Household household) =>
        household.Members.OrderBy(member => member.CreatedAt).ThenBy(member => member.Id.Value);
}
