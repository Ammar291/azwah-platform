using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Assignments;

public sealed class AssignmentPlannerTests
{
    private const string Instrument = "survey";

    private static EligibilityDecision Decision(FamilyMember member, EligibilityOutcome outcome, string section = "A") =>
        new(
            member.Id, Instrument, section, outcome, RuleSetId.New(), "rule", 1,
            outcome switch
            {
                EligibilityOutcome.Eligible => EligibilityReasons.Eligible,
                EligibilityOutcome.Ineligible => EligibilityReasons.AgeBelowMinimum,
                _ => EligibilityReasons.MissingInformation,
            },
            [], Now);

    /// <summary>An assignment of section A of a fresh approved version (so every call is "another version"), moved to <paramref name="status"/>.</summary>
    private static ExistingAssignment Existing(FamilyMember member, AssignmentStatus status, string section = "A")
    {
        var version = ApprovedVersion();
        var assignment = Assign(member, version, version.Sections[0].Id);
        switch (status)
        {
            case AssignmentStatus.InProgress:
                Ok(AssessmentSession.Start(assignment, Now));
                break;
            case AssignmentStatus.Completed:
                var session = Ok(AssessmentSession.Start(assignment, Now));
                foreach (var item in version.Sections[0].Items)
                {
                    Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
                }

                Ok(session.Submit(version, assignment, Now));
                break;
            case AssignmentStatus.Cancelled:
                Ok(assignment.Cancel(Now));
                break;
            case AssignmentStatus.Expired:
                Ok(assignment.Expire(Now));
                break;
        }

        Assert.Equal(status, assignment.Status);
        return new ExistingAssignment(assignment, Instrument, section);
    }

    private static FamilyMember NewMember() => NewHousehold(Head(), Adult(30)).Members[1];

    [Fact]
    public void An_eligible_member_without_an_assignment_gets_one()
    {
        var member = NewMember();

        var plan = AssignmentPlanner.Plan([Decision(member, EligibilityOutcome.Eligible)], []);

        var created = Assert.Single(plan.Create);
        Assert.Equal(member.Id, created.Decision.MemberId);
        Assert.Equal(AssignmentReason.EligibilityRule, created.Reason);
        Assert.Empty(plan.Cancel);
        Assert.Empty(plan.Keep);
    }

    [Theory]
    [InlineData(AssignmentStatus.Cancelled)]
    [InlineData(AssignmentStatus.Expired)]
    public void A_member_eligible_again_after_a_cancelled_or_expired_assignment_gets_a_reissue(AssignmentStatus finished)
    {
        var member = NewMember();

        var plan = AssignmentPlanner.Plan([Decision(member, EligibilityOutcome.Eligible)], [Existing(member, finished)]);

        Assert.Equal(AssignmentReason.Reissue, Assert.Single(plan.Create).Reason);
    }

    [Theory]
    [InlineData(AssignmentStatus.Assigned)]
    [InlineData(AssignmentStatus.InProgress)]
    [InlineData(AssignmentStatus.Completed)]
    public void An_eligible_member_with_a_live_or_completed_assignment_gets_no_duplicate(AssignmentStatus status)
    {
        var member = NewMember();
        var existing = Existing(member, status);

        var plan = AssignmentPlanner.Plan([Decision(member, EligibilityOutcome.Eligible)], [existing]);

        Assert.Empty(plan.Create);
        Assert.Same(existing, Assert.Single(plan.Keep));
    }

    [Fact]
    public void A_module_completed_under_an_older_version_is_not_assigned_again_under_a_newer_one()
    {
        var member = NewMember();
        var completedUnderOlderVersion = Existing(member, AssignmentStatus.Completed);

        var plan = AssignmentPlanner.Plan([Decision(member, EligibilityOutcome.Eligible)], [completedUnderOlderVersion]);

        Assert.Empty(plan.Create);
    }

    [Fact]
    public void A_member_who_no_longer_qualifies_loses_a_not_started_assignment()
    {
        var member = NewMember();
        var existing = Existing(member, AssignmentStatus.Assigned);

        var plan = AssignmentPlanner.Plan([Decision(member, EligibilityOutcome.Ineligible)], [existing]);

        Assert.Same(existing, Assert.Single(plan.Cancel));
        Assert.Empty(plan.Create);
    }

    [Theory]
    [InlineData(AssignmentStatus.InProgress)]
    [InlineData(AssignmentStatus.Completed)]
    public void A_member_who_no_longer_qualifies_keeps_a_started_or_completed_assignment(AssignmentStatus status)
    {
        var member = NewMember();
        var existing = Existing(member, status);

        var plan = AssignmentPlanner.Plan([Decision(member, EligibilityOutcome.Ineligible)], [existing]);

        Assert.Empty(plan.Cancel);
        Assert.Same(existing, Assert.Single(plan.Keep));
    }

    [Fact]
    public void An_indeterminate_member_keeps_what_they_have_and_gets_nothing_new()
    {
        var member = NewMember();
        var existing = Existing(member, AssignmentStatus.Assigned);

        var withAssignment = AssignmentPlanner.Plan([Decision(member, EligibilityOutcome.Indeterminate)], [existing]);
        var without = AssignmentPlanner.Plan([Decision(NewMember(), EligibilityOutcome.Indeterminate)], []);

        Assert.Same(existing, Assert.Single(withAssignment.Keep));
        Assert.Empty(withAssignment.Cancel);
        Assert.Empty(without.Create);
    }

    [Fact]
    public void Assignments_for_another_module_or_member_do_not_count()
    {
        var member = NewMember();

        var plan = AssignmentPlanner.Plan(
            [Decision(member, EligibilityOutcome.Eligible, "A")],
            [Existing(member, AssignmentStatus.Completed, section: "C"), Existing(NewMember(), AssignmentStatus.Assigned)]);

        Assert.Single(plan.Create);
    }

    [Fact]
    public void A_member_eligible_for_two_modules_gets_two_assignments()
    {
        var member = NewMember();

        var plan = AssignmentPlanner.Plan(
            [Decision(member, EligibilityOutcome.Eligible, "A"), Decision(member, EligibilityOutcome.Eligible, "C")], []);

        Assert.Equal(new[] { "A", "C" }, plan.Create.Select(created => created.Decision.SectionCode));
    }
}
