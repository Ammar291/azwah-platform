using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Households;

public sealed class HouseholdEnrolmentTests
{
    private static readonly PlatformUserId Manager = PlatformUserId.New();

    private static Household Enrolled(int selfIndex = 0, params FamilyMemberDetails[] roster) =>
        Ok(Household.Enrol(
            HouseholdReference.Generate(),
            roster.Length == 0 ? [Head(), Adult(39, relationship: HouseholdRelationship.Spouse), Child(9)] : roster,
            selfIndex,
            Manager,
            Now));

    [Fact]
    public void Enrol_links_the_self_member_as_the_active_manager()
    {
        var household = Enrolled(1);

        var membership = Assert.Single(household.Memberships);
        Assert.Equal(
            (Manager, household.Members[1].Id, HouseholdRole.Manager, MembershipStatus.Active, household.Id),
            (membership.PlatformUserId, membership.FamilyMemberId, membership.Role, membership.Status, membership.HouseholdId));
        Assert.True(household.IsManagedBy(Manager));
        Assert.Null(household.RosterConfirmedAt);
    }

    [Fact]
    public void Enrol_refuses_a_manager_under_18() =>
        Assert.Equal(
            HouseholdErrors.ManagerUnder18,
            Household.Enrol(HouseholdReference.Generate(), [Head(), Child(17)], 1, Manager, Now).Error);

    // Only the birth month is stored: someone whose 18th birthday falls this month may still be 17 today.
    [Fact]
    public void Enrol_refuses_a_manager_who_turns_18_this_month() =>
        Assert.Equal(
            HouseholdErrors.ManagerUnder18,
            Household.Enrol(
                HouseholdReference.Generate(), [Head(), Adult(18, relationship: HouseholdRelationship.Child)], 1, Manager, Now).Error);

    [Fact]
    public void An_account_is_not_linked_to_a_member_who_turns_18_this_month()
    {
        var household = Enrolled(0, Head(), Adult(18, relationship: HouseholdRelationship.Child), Adult(19, relationship: HouseholdRelationship.Child));

        Assert.Equal(
            HouseholdErrors.AccountHolderUnder18,
            household.LinkAccount(PlatformUserId.New(), household.Members[1].Id, Now).Error);
        Assert.True(household.LinkAccount(PlatformUserId.New(), household.Members[2].Id, Now).IsSuccess);
    }

    [Fact]
    public void A_member_with_their_own_account_cannot_be_corrected_to_turning_18_this_month()
    {
        var household = Enrolled();
        var spouse = household.Members[1].Id;
        Ok(household.LinkAccount(PlatformUserId.New(), spouse, Now));

        Assert.Equal(
            HouseholdErrors.AccountHolderUnder18,
            household.UpdateMember(spouse, Adult(18, relationship: HouseholdRelationship.Spouse), Now).Error);
    }

    [Fact]
    public void Enrol_reports_roster_errors_before_anything_else() =>
        Assert.Equal(HouseholdErrors.RosterEmpty, Household.Enrol(HouseholdReference.Generate(), [], 0, Manager, Now).Error);

    [Fact]
    public void The_manager_acts_for_their_own_member_and_members_without_an_account()
    {
        var household = Enrolled();

        Assert.Equal(household.Members.Select(member => member.Id), household.MembersActableBy(Manager));
        Assert.All(household.Members, member => Assert.True(household.MayActFor(Manager, member.Id)));
    }

    [Fact]
    public void Only_the_account_linked_to_the_active_head_is_the_head_of_household()
    {
        var headManaged = Enrolled();
        var spouseManaged = Enrolled(1);
        var spouseUser = PlatformUserId.New();
        Ok(headManaged.LinkAccount(spouseUser, headManaged.Members[1].Id, Now));

        Assert.True(headManaged.IsHeadOfHousehold(Manager));
        Assert.False(headManaged.IsHeadOfHousehold(spouseUser));
        Assert.False(spouseManaged.IsHeadOfHousehold(Manager));
        Assert.False(headManaged.IsHeadOfHousehold(PlatformUserId.New()));
    }

    [Fact]
    public void Another_user_acts_for_nobody()
    {
        var household = Enrolled();

        Assert.Empty(household.MembersActableBy(PlatformUserId.New()));
        Assert.False(household.IsManagedBy(PlatformUserId.New()));
    }

    [Fact]
    public void A_member_with_their_own_account_is_out_of_the_managers_reach()
    {
        var household = Enrolled();
        var spouse = household.Members[1].Id;
        var spouseUser = PlatformUserId.New();

        var membership = Ok(household.LinkAccount(spouseUser, spouse, Now));

        Assert.Equal(HouseholdRole.Member, membership.Role);
        Assert.False(household.MayActFor(Manager, spouse));
        Assert.DoesNotContain(spouse, household.MembersActableBy(Manager));
        Assert.Equal([spouse], household.MembersActableBy(spouseUser));
        Assert.False(household.IsManagedBy(spouseUser));
    }

    [Fact]
    public void An_account_needs_an_adult_member_without_one_and_a_user_without_a_membership()
    {
        var household = Enrolled();
        var spouse = household.Members[1].Id;
        Ok(household.LinkAccount(PlatformUserId.New(), spouse, Now));

        Assert.Equal(HouseholdErrors.MemberAlreadyLinked, household.LinkAccount(PlatformUserId.New(), spouse, Now).Error);
        Assert.Equal(HouseholdErrors.AccountHolderUnder18, household.LinkAccount(PlatformUserId.New(), household.Members[2].Id, Now).Error);
        Assert.Equal(HouseholdErrors.MemberAlreadyLinked, household.LinkAccount(Manager, household.Members[0].Id, Now).Error);
        Assert.Equal(HouseholdErrors.MemberNotFound, household.LinkAccount(PlatformUserId.New(), FamilyMemberId.New(), Now).Error);
    }

    [Fact]
    public void Removing_a_member_with_an_account_ends_their_membership()
    {
        var household = Enrolled();
        var spouse = household.Members[1].Id;
        var spouseUser = PlatformUserId.New();
        Ok(household.LinkAccount(spouseUser, spouse, Now));

        Ok(household.DeactivateMember(spouse, Now));

        Assert.Null(household.FindActiveMembership(spouseUser));
        Assert.Empty(household.MembersActableBy(spouseUser));
    }

    [Fact]
    public void The_manager_cannot_act_for_an_inactive_member()
    {
        var household = Enrolled();
        var spouse = household.Members[1].Id;

        Ok(household.DeactivateMember(spouse, Now));

        Assert.False(household.MayActFor(Manager, spouse));
    }

    [Fact]
    public void The_managers_own_member_cannot_be_deactivated()
    {
        var household = Enrolled();

        Assert.Equal(HouseholdErrors.MemberIsManager, household.DeactivateMember(household.Members[0].Id, Now).Error);
        Assert.True(household.Members[0].IsActive);
    }

    [Fact]
    public void The_managers_member_cannot_be_made_younger_than_18()
    {
        var household = Enrolled(1);
        var self = household.Members[1];

        Assert.Equal(
            HouseholdErrors.ManagerUnder18,
            household.UpdateMember(self.Id, Adult(17, relationship: HouseholdRelationship.Spouse), Now).Error);
        Assert.Equal(39, self.AgeOn(Today));
    }

    [Fact]
    public void A_member_with_their_own_account_cannot_be_made_younger_than_18()
    {
        var household = Enrolled();
        var spouse = household.Members[1].Id;
        Ok(household.LinkAccount(PlatformUserId.New(), spouse, Now));

        Assert.Equal(
            HouseholdErrors.AccountHolderUnder18,
            household.UpdateMember(spouse, Adult(17, relationship: HouseholdRelationship.Spouse), Now).Error);
    }

    [Fact]
    public void A_member_without_an_account_may_be_corrected_to_under_18()
    {
        var household = Enrolled();

        Ok(household.UpdateMember(household.Members[1].Id, Adult(17, relationship: HouseholdRelationship.Child), Now));
    }

    [Fact]
    public void A_complete_roster_is_confirmed_and_raises_roster_confirmed()
    {
        var household = Enrolled();
        household.ClearDomainEvents();

        Ok(household.ConfirmRoster(Now));

        Assert.Equal(Now, household.RosterConfirmedAt);
        Assert.Equal(household.Id, Assert.IsType<RosterConfirmed>(Assert.Single(household.GetDomainEvents())).HouseholdId);
    }

    [Fact]
    public void Confirmation_needs_an_active_head() =>
        Assert.Equal(
            HouseholdErrors.RosterHasNoHead,
            Enrolled(0, Adult(40, earner: true, relationship: HouseholdRelationship.Spouse)).ConfirmRoster(Now).Error);

    [Fact]
    public void Confirmation_needs_a_relationship_for_every_other_member() =>
        Assert.Equal(
            HouseholdErrors.RosterMissingRelationship,
            Enrolled(0, Head(), Adult(39, relationship: null)).ConfirmRoster(Now).Error);

    [Fact]
    public void Confirmation_needs_an_employment_status_for_every_adult() =>
        Assert.Equal(
            HouseholdErrors.RosterMissingEmploymentStatus,
            Enrolled(0, Head(), Adult(39, relationship: HouseholdRelationship.Spouse, employment: null)).ConfirmRoster(Now).Error);

    [Fact]
    public void Children_need_no_employment_status()
    {
        var household = Enrolled(0, Head(), Child(9));

        Ok(household.ConfirmRoster(Now));
    }

    [Fact]
    public void An_inactive_member_does_not_block_confirmation()
    {
        var household = Enrolled(0, Head(), Adult(39, relationship: HouseholdRelationship.Spouse));
        var incomplete = Ok(household.AddMember(Adult(30, relationship: null), Now));
        Ok(household.DeactivateMember(incomplete.Id, Now));

        Ok(household.ConfirmRoster(Now));
    }

    [Fact]
    public void Every_roster_change_clears_the_confirmation()
    {
        var household = Enrolled();
        var spouse = household.Members[1].Id;

        Ok(household.ConfirmRoster(Now));
        Ok(household.AddMember(Child(4), Now));
        Assert.Null(household.RosterConfirmedAt);

        Ok(household.ConfirmRoster(Now));
        Ok(household.UpdateMember(spouse, Adult(40, relationship: HouseholdRelationship.Spouse), Now));
        Assert.Null(household.RosterConfirmedAt);

        Ok(household.ConfirmRoster(Now));
        Ok(household.DeactivateMember(spouse, Now));
        Assert.Null(household.RosterConfirmedAt);
    }

    [Fact]
    public void A_withdrawn_household_cannot_confirm_its_roster()
    {
        var household = Enrolled();
        Ok(household.Withdraw(Now));

        Assert.Equal(HouseholdErrors.NotActive, household.ConfirmRoster(Now).Error);
    }
}
