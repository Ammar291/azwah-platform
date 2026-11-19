using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Questionnaires;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Assignments;

public sealed class AssessmentAssignmentTests
{
    [Fact]
    public void Assignment_records_member_version_section_wave_and_rule_set()
    {
        var member = NewHousehold().Members[0];
        var version = ApprovedVersion();
        var wave = OpenWave();
        var rules = ApprovedRuleSet(RuleSetKind.Eligibility, version);
        var sectionId = version.Sections[1].Id;

        var assignment = Ok(AssessmentAssignment.Assign(
            member, version, sectionId, wave, rules, AssignmentReason.EligibilityRule, Now.AddDays(14), Now));

        Assert.Equal(member.Id, assignment.FamilyMemberId);
        Assert.Equal(version.Id, assignment.InstrumentVersionId);
        Assert.Equal(sectionId, assignment.InstrumentSectionId);
        Assert.Equal(wave.Id, assignment.WaveId);
        Assert.Equal(rules.Id, assignment.EligibilityRuleSetId);
        Assert.Equal(AssignmentStatus.Assigned, assignment.Status);
        Assert.Equal(Now.AddDays(14), assignment.ExpiresAt);
        Assert.IsType<AssessmentAssigned>(Assert.Single(assignment.GetDomainEvents()));
    }

    [Fact]
    public void Assign_rejects_a_member_under_18()
    {
        var household = NewHousehold(Head(), Child(17));
        var version = ApprovedVersion();

        Assert.Equal(
            AssignmentErrors.MemberUnder18,
            AssessmentAssignment.Assign(household.Members[1], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version), AssignmentReason.EligibilityRule, null, Now).Error);
    }

    // Only the birth month is stored: a member whose 18th birthday falls this month may still be 17 today.
    [Fact]
    public void Assign_rejects_a_member_who_turns_18_this_month()
    {
        var household = NewHousehold(Head(), Adult(18, relationship: FamilyResilience.Domain.Members.HouseholdRelationship.Child));
        var version = ApprovedVersion();

        Assert.Equal(
            AssignmentErrors.MemberUnder18,
            AssessmentAssignment.Assign(household.Members[1], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version), AssignmentReason.EligibilityRule, null, Now).Error);
    }

    [Fact]
    public void Assign_accepts_a_member_from_the_month_after_they_turn_18()
    {
        var turned18LastMonth = Ok(FamilyResilience.Domain.Common.YearMonth.Create(Today.AddMonths(-1).Year - 18, Today.AddMonths(-1).Month, Today));
        var household = NewHousehold(Head(), new FamilyResilience.Domain.Members.FamilyMemberDetails(
            turned18LastMonth, FamilyResilience.Domain.Members.HouseholdRelationship.Child, FamilyResilience.Domain.Members.EmploymentStatus.Student, false, false));
        var version = ApprovedVersion();

        Assert.True(AssessmentAssignment.Assign(household.Members[1], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version), AssignmentReason.EligibilityRule, null, Now).IsSuccess);
    }

    [Fact]
    public void Assign_rejects_an_inactive_member()
    {
        var household = NewHousehold(Head(), Adult(39, relationship: FamilyResilience.Domain.Members.HouseholdRelationship.Spouse));
        var spouse = household.Members[1];
        Ok(household.DeactivateMember(spouse.Id, Now));
        var version = ApprovedVersion();

        Assert.Equal(
            AssignmentErrors.MemberNotActive,
            AssessmentAssignment.Assign(spouse, version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version), AssignmentReason.EligibilityRule, null, Now).Error);
    }

    [Fact]
    public void Assign_rejects_a_draft_version()
    {
        var draft = DraftVersion();
        var approved = ApprovedVersion();

        Assert.Equal(
            AssignmentErrors.VersionNotEffective,
            AssessmentAssignment.Assign(NewHousehold().Members[0], draft, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, approved), AssignmentReason.EligibilityRule, null, Now).Error);
    }

    [Fact]
    public void Assign_rejects_a_section_of_another_version()
    {
        var version = ApprovedVersion();
        var other = DraftVersion();

        Assert.Equal(
            AssignmentErrors.SectionNotInVersion,
            AssessmentAssignment.Assign(NewHousehold().Members[0], version, other.Sections[0].Id, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version), AssignmentReason.EligibilityRule, null, Now).Error);
    }

    [Fact]
    public void Assign_rejects_a_closed_wave()
    {
        var version = ApprovedVersion();
        var wave = OpenWave();
        Ok(wave.Close(Now));

        Assert.Equal(
            AssignmentErrors.WaveClosed,
            AssessmentAssignment.Assign(NewHousehold().Members[0], version, null, wave, ApprovedRuleSet(RuleSetKind.Eligibility, version), AssignmentReason.EligibilityRule, null, Now).Error);
    }

    [Fact]
    public void Assign_needs_an_approved_eligibility_rule_set_for_the_same_version()
    {
        var member = NewHousehold().Members[0];
        var version = ApprovedVersion();
        var proposed = Ok(RuleSet.Propose(RuleSetKind.Eligibility, "rules", 1, version, "{}", Now));
        var scoring = ApprovedRuleSet(RuleSetKind.Scoring, version);
        var otherVersion = ApprovedVersion();
        var otherRules = ApprovedRuleSet(RuleSetKind.Eligibility, otherVersion);

        Assert.Equal(AssignmentErrors.RuleSetNotApproved, AssessmentAssignment.Assign(member, version, null, OpenWave(), proposed, AssignmentReason.EligibilityRule, null, Now).Error);
        Assert.Equal(AssignmentErrors.RuleSetWrongKind, AssessmentAssignment.Assign(member, version, null, OpenWave(), scoring, AssignmentReason.EligibilityRule, null, Now).Error);
        Assert.Equal(AssignmentErrors.RuleSetVersionMismatch, AssessmentAssignment.Assign(member, version, null, OpenWave(), otherRules, AssignmentReason.EligibilityRule, null, Now).Error);
    }

    [Fact]
    public void Expiry_must_be_after_now()
    {
        var version = ApprovedVersion();

        Assert.Equal(
            AssignmentErrors.ExpiryInvalid,
            AssessmentAssignment.Assign(NewHousehold().Members[0], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version), AssignmentReason.EligibilityRule, Now, Now).Error);
    }

    [Fact]
    public void Expired_or_cancelled_assignment_cannot_change_again()
    {
        var version = ApprovedVersion();
        var expired = Assign(NewHousehold().Members[0], version);
        var cancelled = Assign(NewHousehold().Members[0], version);

        Assert.True(expired.Expire(Now).IsSuccess);
        Assert.True(cancelled.Cancel(Now).IsSuccess);

        Assert.Equal(AssignmentStatus.Expired, expired.Status);
        Assert.Equal(AssignmentStatus.Cancelled, cancelled.Status);
        Assert.Equal(AssignmentErrors.AlreadyFinished, expired.Cancel(Now).Error);
        Assert.Equal(AssignmentErrors.AlreadyFinished, cancelled.Expire(Now).Error);
    }

    private static AssessmentAssignment ExpiringInAnHour()
    {
        var version = ApprovedVersion();
        return Ok(AssessmentAssignment.Assign(
            NewHousehold().Members[0], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version),
            AssignmentReason.EligibilityRule, Now.AddHours(1), Now));
    }

    [Fact]
    public void An_assignment_is_overdue_from_its_expiry_until_it_is_marked_expired()
    {
        var assignment = ExpiringInAnHour();

        Assert.False(assignment.IsOverdueAt(Now.AddMinutes(59)));
        Assert.False(assignment.HasExpiredAt(Now.AddMinutes(59)));
        Assert.True(assignment.IsOverdueAt(Now.AddHours(1)));
        Assert.True(assignment.HasExpiredAt(Now.AddHours(1)));

        Ok(assignment.Expire(Now.AddHours(2)));

        Assert.Equal(AssignmentStatus.Expired, assignment.Status);
        Assert.False(assignment.IsOverdueAt(Now.AddHours(2)));
        Assert.True(assignment.HasExpiredAt(Now.AddHours(2)));
        Assert.True(assignment.HasExpiredAt(Now));
    }

    [Fact]
    public void An_assignment_without_an_expiry_is_never_overdue()
    {
        var assignment = Assign(NewHousehold().Members[0], ApprovedVersion());

        Assert.Null(assignment.ExpiresAt);
        Assert.False(assignment.IsOverdueAt(Now.AddYears(5)));
        Assert.False(assignment.HasExpiredAt(Now.AddYears(5)));
    }

    [Fact]
    public void A_cancelled_assignment_is_not_overdue_after_its_expiry()
    {
        var assignment = ExpiringInAnHour();
        Ok(assignment.Cancel(Now));

        Assert.False(assignment.IsOverdueAt(Now.AddHours(2)));
    }
}
