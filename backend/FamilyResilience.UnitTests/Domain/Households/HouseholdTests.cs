using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Households;

public sealed class HouseholdTests
{
    [Fact]
    public void Create_adds_active_members_and_raises_household_created()
    {
        var result = Household.Create(
            HouseholdReference.Generate(),
            [Head(), Adult(39, relationship: HouseholdRelationship.Spouse), Child(9)],
            Now);

        Assert.True(result.IsSuccess);
        var household = result.Value;
        Assert.Equal(HouseholdStatus.Active, household.Status);
        Assert.Equal(Now, household.CreatedAt);
        Assert.Equal(3, household.Members.Count);
        Assert.All(household.Members, member =>
        {
            Assert.True(member.IsActive);
            Assert.Equal(household.Id, member.HouseholdId);
            Assert.Equal(7, member.Id.Value.Version);
        });
        var raised = Assert.IsType<HouseholdCreated>(Assert.Single(household.GetDomainEvents()));
        Assert.Equal(household.Id, raised.HouseholdId);
    }

    [Fact]
    public void Create_rejects_an_empty_roster() =>
        Assert.Equal(HouseholdErrors.RosterEmpty, Household.Create(HouseholdReference.Generate(), [], Now).Error);

    [Fact]
    public void Create_rejects_a_time_that_is_not_utc() =>
        Assert.Equal(
            CommonErrors.TimeNotUtc,
            Household.Create(HouseholdReference.Generate(), [Head()], Now.ToOffset(TimeSpan.FromHours(4))).Error);

    [Fact]
    public void Create_rejects_more_members_than_a_roster_holds() =>
        Assert.Equal(
            HouseholdErrors.RosterFull,
            Household.Create(HouseholdReference.Generate(), [Head(), .. Enumerable.Repeat(Child(9), Household.MaxActiveMembers)], Now).Error);

    [Fact]
    public void A_full_roster_takes_another_member_only_after_one_leaves()
    {
        var household = NewHousehold([Head(), .. Enumerable.Repeat(Child(9), Household.MaxActiveMembers - 1)]);

        Assert.Equal(HouseholdErrors.RosterFull, household.AddMember(Child(4), Now).Error);

        Ok(household.DeactivateMember(household.Members[1].Id, Now));
        Ok(household.AddMember(Child(4), Now));
        Assert.Equal(Household.MaxActiveMembers + 1, household.Members.Count);
    }

    [Fact]
    public void A_second_active_head_is_rejected() =>
        Assert.Equal(HouseholdErrors.HeadAlreadyExists, NewHousehold(Head()).AddMember(Adult(head: true), Now).Error);

    [Fact]
    public void A_second_primary_earner_is_rejected() =>
        Assert.Equal(
            HouseholdErrors.PrimaryEarnerAlreadyExists,
            NewHousehold(Head()).AddMember(Adult(earner: true, relationship: HouseholdRelationship.Spouse), Now).Error);

    [Fact]
    public void A_head_under_18_is_rejected() =>
        Assert.Equal(
            HouseholdErrors.HeadUnder18,
            Household.Create(HouseholdReference.Generate(), [Adult(17, head: true)], Now).Error);

    [Fact]
    public void A_primary_earner_under_18_is_rejected() =>
        Assert.Equal(
            HouseholdErrors.PrimaryEarnerUnder18,
            NewHousehold(Adult(45, head: true)).AddMember(Adult(17, earner: true, relationship: HouseholdRelationship.Child), Now).Error);

    [Fact]
    public void A_member_turning_18_this_month_may_be_the_primary_earner() =>
        Assert.True(NewHousehold(Adult(45, head: true))
            .AddMember(Adult(18, earner: true, relationship: HouseholdRelationship.Child), Now).IsSuccess);

    [Fact]
    public void The_head_cannot_have_a_relationship_to_the_head() =>
        Assert.Equal(
            HouseholdErrors.HeadHasRelationship,
            Household.Create(HouseholdReference.Generate(), [Adult(head: true, relationship: HouseholdRelationship.Spouse)], Now).Error);

    [Fact]
    public void A_deactivated_head_can_be_replaced()
    {
        var household = NewHousehold(Head(), Adult(39, relationship: HouseholdRelationship.Spouse));
        var head = household.Members[0];

        Assert.True(household.DeactivateMember(head.Id, Now).IsSuccess);

        Assert.True(household.AddMember(Head(50), Now).IsSuccess);
        Assert.Equal(3, household.Members.Count);
        Assert.False(head.IsActive);
    }

    [Fact]
    public void Updating_a_member_keeps_its_own_flags_but_cannot_take_another_members()
    {
        var household = NewHousehold(Head(), Adult(39, relationship: HouseholdRelationship.Spouse));
        var head = household.Members[0];
        var spouse = household.Members[1];

        Assert.True(household.UpdateMember(head.Id, Head(43), Now).IsSuccess);
        Assert.Equal(
            HouseholdErrors.HeadAlreadyExists,
            household.UpdateMember(spouse.Id, Adult(39, head: true), Now).Error);
    }

    [Fact]
    public void Every_roster_change_moves_the_household_updated_at()
    {
        var household = NewHousehold(Head());
        var later = Now.AddHours(1);

        Assert.True(household.AddMember(Child(9), later).IsSuccess);

        Assert.Equal(later, household.UpdatedAt);
        Assert.Equal(Now, household.CreatedAt);
    }

    [Fact]
    public void Deactivating_keeps_the_member_and_raises_family_member_deactivated()
    {
        var household = NewHousehold(Head(), Child(9));
        var child = household.Members[1];
        household.ClearDomainEvents();

        Assert.True(household.DeactivateMember(child.Id, Now).IsSuccess);

        Assert.Equal(MemberStatus.Inactive, household.FindMember(child.Id)!.Status);
        var raised = Assert.IsType<FamilyMemberDeactivated>(Assert.Single(household.GetDomainEvents()));
        Assert.Equal(child.Id, raised.FamilyMemberId);
    }

    [Fact]
    public void Deactivating_an_unknown_member_is_rejected() =>
        Assert.Equal(HouseholdErrors.MemberNotFound, NewHousehold().DeactivateMember(FamilyMemberId.New(), Now).Error);

    [Fact]
    public void A_withdrawn_household_rejects_roster_changes()
    {
        var household = NewHousehold();
        household.ClearDomainEvents();

        Assert.True(household.Withdraw(Now).IsSuccess);

        Assert.Equal(HouseholdStatus.Withdrawn, household.Status);
        Assert.IsType<HouseholdWithdrawn>(Assert.Single(household.GetDomainEvents()));
        Assert.Equal(HouseholdErrors.NotActive, household.AddMember(Child(4), Now).Error);
        Assert.Equal(HouseholdErrors.AlreadyWithdrawn, household.Withdraw(Now).Error);
    }

    [Fact]
    public void Child_presence_is_derived_from_active_members_on_a_date()
    {
        var household = NewHousehold(Head(), Child(17));

        Assert.True(household.HasActiveMemberUnder18On(Today));
        Assert.True(household.HasActiveMemberUnder18On(new DateOnly(Today.Year + 1, Today.Month, 1).AddDays(-1)));
        Assert.False(household.HasActiveMemberUnder18On(new DateOnly(Today.Year + 1, Today.Month, 1)));
    }

    [Fact]
    public void Undefined_relationship_is_rejected() =>
        Assert.Equal(
            HouseholdErrors.RelationshipInvalid,
            NewHousehold().AddMember(Adult(relationship: (HouseholdRelationship)99), Now).Error);

    [Fact]
    public void Birth_month_after_now_is_rejected()
    {
        var future = Ok(YearMonth.Create(2026, 12, new DateOnly(2027, 1, 1)));

        Assert.Equal(
            HouseholdErrors.BirthMonthInFuture,
            NewHousehold().AddMember(new FamilyMemberDetails(future, HouseholdRelationship.Child, null, false, false), Now).Error);
    }
}
