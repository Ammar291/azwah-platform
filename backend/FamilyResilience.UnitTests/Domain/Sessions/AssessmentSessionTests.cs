using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Sessions;

public sealed class AssessmentSessionTests
{
    private static (AssessmentSession Session, InstrumentVersion Version, AssessmentAssignment Assignment) Started(
        bool sectionB = false)
    {
        var version = ApprovedVersion();
        var assignment = Assign(NewHousehold().Members[0], version, sectionB ? version.Sections[1].Id : null);
        return (Ok(AssessmentSession.Start(assignment, Now)), version, assignment);
    }

    [Fact]
    public void Start_copies_the_assignment_and_moves_it_to_in_progress()
    {
        var (session, version, assignment) = Started(sectionB: true);

        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Equal(assignment.Id, session.AssessmentAssignmentId);
        Assert.Equal(assignment.FamilyMemberId, session.FamilyMemberId);
        Assert.Equal(version.Id, session.InstrumentVersionId);
        Assert.Equal(version.Sections[1].Id, session.InstrumentSectionId);
        Assert.Equal(AssignmentStatus.InProgress, assignment.Status);
    }

    [Fact]
    public void Start_rejects_an_assignment_that_already_has_a_session()
    {
        var (_, _, assignment) = Started();

        Assert.Equal(AssignmentErrors.NotAssigned, AssessmentSession.Start(assignment, Now).Error);
    }

    [Fact]
    public void Start_rejects_an_expired_assignment()
    {
        var version = ApprovedVersion();
        var assignment = Ok(AssessmentAssignment.Assign(
            NewHousehold().Members[0], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version),
            AssignmentReason.EligibilityRule, Now.AddHours(1), Now));

        Assert.Equal(AssignmentErrors.AssignmentExpired, AssessmentSession.Start(assignment, Now.AddHours(2)).Error);
    }

    // Once maintenance has marked it Expired, starting answers as it did just before: the time has passed.
    [Fact]
    public void Start_reports_an_assignment_marked_expired_as_expired_not_as_waiting_to_start()
    {
        var assignment = Assign(NewHousehold().Members[0], ApprovedVersion());
        Ok(assignment.Expire(Now));

        Assert.Equal(AssignmentErrors.AssignmentExpired, AssessmentSession.Start(assignment, Now).Error);
        Assert.Equal(AssignmentStatus.Expired, assignment.Status);
    }

    [Fact]
    public void Answer_records_one_response_with_the_option_value()
    {
        var (session, version, _) = Started();
        var item = version.Sections[0].Items[0];
        var option = item.Options[2];

        Assert.True(session.Answer(version, item.Id, option.Id, Now).IsSuccess);

        var response = Assert.Single(session.Responses);
        Assert.Equal(item.Id, response.InstrumentItemId);
        Assert.Equal(option.Id, response.ResponseOptionId);
        Assert.Equal(3, response.RawValue);
        Assert.Equal(version.Id, response.InstrumentVersionId);
    }

    [Fact]
    public void Changing_an_answer_replaces_the_response()
    {
        var (session, version, _) = Started();
        var item = version.Sections[0].Items[0];
        var later = Now.AddMinutes(5);

        Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        Assert.True(session.Answer(version, item.Id, item.Options[1].Id, later).IsSuccess);

        var response = Assert.Single(session.Responses);
        Assert.Equal(2, response.RawValue);
        Assert.Equal(Now, response.AnsweredAt);
        Assert.Equal(later, response.LastModifiedAt);
        Assert.Equal(later, session.UpdatedAt);
    }

    [Fact]
    public void Answer_rejects_an_option_from_another_item()
    {
        var (session, version, _) = Started();
        var item = version.Sections[0].Items[0];
        var otherItem = version.Sections[0].Items[1];

        Assert.Equal(SessionErrors.OptionNotInItem, session.Answer(version, item.Id, otherItem.Options[0].Id, Now).Error);
    }

    [Fact]
    public void Answer_rejects_another_version()
    {
        var (session, _, _) = Started();
        var other = ApprovedVersion();
        var item = other.Sections[0].Items[0];

        Assert.Equal(SessionErrors.VersionMismatch, session.Answer(other, item.Id, item.Options[0].Id, Now).Error);
    }

    [Fact]
    public void Answer_rejects_an_unknown_item()
    {
        var (session, version, _) = Started();

        Assert.Equal(
            SessionErrors.ItemNotInVersion,
            session.Answer(version, InstrumentItemId.New(), ResponseOptionId.New(), Now).Error);
    }

    [Fact]
    public void Answer_rejects_an_item_outside_the_assigned_section()
    {
        var (session, version, _) = Started(sectionB: true);
        var itemInA = version.Sections[0].Items[0];

        Assert.Equal(SessionErrors.ItemNotInSection, session.Answer(version, itemInA.Id, itemInA.Options[0].Id, Now).Error);
        Assert.Empty(session.Responses);
    }

    [Fact]
    public void Answer_with_a_time_before_the_last_change_is_rejected()
    {
        var (session, version, _) = Started();
        var item = version.Sections[0].Items[0];

        Assert.Equal(
            SessionErrors.TimeBeforeLastChange,
            session.Answer(version, item.Id, item.Options[0].Id, Now.AddSeconds(-1)).Error);
    }

    [Fact]
    public void Submit_requires_every_required_item_of_the_version()
    {
        var (session, version, assignment) = Started();
        foreach (var item in version.Sections[0].Items)
        {
            Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        }

        Assert.Equal(SessionErrors.RequiredItemsUnanswered, session.Submit(version, assignment, Now).Error);
        Assert.Equal(SessionStatus.InProgress, session.Status);
    }

    [Fact]
    public void Module_session_needs_only_the_items_of_its_section()
    {
        var (session, version, assignment) = Started(sectionB: true);
        foreach (var item in version.Sections[1].Items)
        {
            Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        }

        Assert.True(session.Submit(version, assignment, Now).IsSuccess);
    }

    [Fact]
    public void Submit_completes_the_assignment_and_raises_session_submitted()
    {
        var (session, _, assignment) = SubmittedSession();

        Assert.Equal(SessionStatus.Submitted, session.Status);
        Assert.Equal(Now, session.SubmittedAt);
        Assert.Equal(AssignmentStatus.Completed, assignment.Status);
        Assert.Equal(Now, assignment.CompletedAt);
        var raised = Assert.IsType<AssessmentSessionSubmitted>(Assert.Single(session.GetDomainEvents()));
        Assert.Equal(session.Id, raised.AssessmentSessionId);
    }

    [Fact]
    public void Submit_rejects_another_assignment()
    {
        var (session, version, _) = Started();
        var other = Assign(NewHousehold().Members[0], version);

        Assert.Equal(SessionErrors.AssignmentMismatch, session.Submit(version, other, Now).Error);
    }

    [Fact]
    public void Submitted_session_rejects_further_answers()
    {
        var (session, version, _) = SubmittedSession();
        var item = version.Sections[0].Items[0];

        Assert.Equal(SessionErrors.NotInProgress, session.Answer(version, item.Id, item.Options[1].Id, Now).Error);
    }

    [Fact]
    public void Abandon_returns_the_assignment_so_a_new_session_can_start()
    {
        var (session, _, assignment) = Started();

        Assert.True(session.Abandon(assignment, Now).IsSuccess);

        Assert.Equal(SessionStatus.Abandoned, session.Status);
        Assert.Equal(Now, session.AbandonedAt);
        Assert.Equal(AssignmentStatus.Assigned, assignment.Status);
        Assert.True(AssessmentSession.Start(assignment, Now).IsSuccess);
    }

    [Fact]
    public void Abandoning_after_the_assignment_was_cancelled_keeps_it_cancelled()
    {
        var (session, _, assignment) = Started();
        var later = Now.AddMinutes(5);
        Ok(assignment.Cancel(later));

        Assert.True(session.Abandon(assignment, later).IsSuccess);

        Assert.Equal(SessionStatus.Abandoned, session.Status);
        Assert.Equal(AssignmentStatus.Cancelled, assignment.Status);
        Assert.Equal(AssignmentErrors.NotAssigned, AssessmentSession.Start(assignment, later).Error);
    }

    [Fact]
    public void Abandoning_after_the_assignment_expired_keeps_it_expired()
    {
        var (session, _, assignment) = Started();
        var later = Now.AddMinutes(5);
        Ok(assignment.Expire(later));

        Assert.True(session.Abandon(assignment, later).IsSuccess);

        Assert.Equal(SessionStatus.Abandoned, session.Status);
        Assert.Equal(AssignmentStatus.Expired, assignment.Status);
        Assert.Equal(AssignmentErrors.AssignmentExpired, AssessmentSession.Start(assignment, later).Error);
    }

    [Fact]
    public void Abandon_rejects_a_session_that_is_not_in_progress()
    {
        var (session, _, assignment) = SubmittedSession();

        Assert.Equal(SessionErrors.NotInProgress, session.Abandon(assignment, Now).Error);
        Assert.Equal(SessionStatus.Submitted, session.Status);
        Assert.Equal(AssignmentStatus.Completed, assignment.Status);
    }

    [Fact]
    public void Abandon_rejects_another_assignment()
    {
        var (session, version, assignment) = Started();
        var other = Assign(NewHousehold().Members[0], version);

        Assert.Equal(SessionErrors.AssignmentMismatch, session.Abandon(other, Now).Error);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Equal(AssignmentStatus.InProgress, assignment.Status);
        Assert.Equal(AssignmentStatus.Assigned, other.Status);
    }

    [Fact]
    public void Answer_time_must_be_utc()
    {
        var (session, version, _) = Started();
        var item = version.Sections[0].Items[0];

        Assert.Equal(
            CommonErrors.TimeNotUtc,
            session.Answer(version, item.Id, item.Options[0].Id, Now.ToOffset(TimeSpan.FromHours(4))).Error);
    }

    [Fact]
    public void The_first_submission_reports_submitted()
    {
        var (session, version, assignment) = Started();
        foreach (var item in version.GetItems())
        {
            Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        }

        Assert.Equal(SubmissionOutcome.Submitted, Ok(session.Submit(version, assignment, Now)));
    }

    [Fact]
    public void Submitting_a_scored_session_is_also_idempotent()
    {
        var (session, version, assignment) = SubmittedSession();
        Ok(ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), SyntheticComputation(session), Now));

        Assert.Equal(SubmissionOutcome.AlreadySubmitted, Ok(session.Submit(version, assignment, Now)));
        Assert.Equal(SessionStatus.Scored, session.Status);
    }

    [Fact]
    public void Submitting_again_is_idempotent_and_changes_nothing()
    {
        var (session, version, assignment) = SubmittedSession();
        session.ClearDomainEvents();

        var again = session.Submit(version, assignment, Now.AddMinutes(10));

        Assert.Equal(SubmissionOutcome.AlreadySubmitted, Ok(again));
        Assert.Equal(SessionStatus.Submitted, session.Status);
        Assert.Equal(Now, session.SubmittedAt);
        Assert.Equal(Now, session.UpdatedAt);
        Assert.Empty(session.GetDomainEvents());
    }

    [Fact]
    public void An_abandoned_session_cannot_be_submitted()
    {
        var (session, version, assignment) = Started();
        Ok(session.Abandon(assignment, Now));

        Assert.Equal(SessionErrors.NotInProgress, session.Submit(version, assignment, Now).Error);
    }

    [Fact]
    public void A_submitted_session_cannot_be_abandoned()
    {
        var (session, _, assignment) = SubmittedSession();

        Assert.Equal(SessionErrors.NotInProgress, session.Abandon(assignment, Now).Error);
        Assert.Equal(SessionStatus.Submitted, session.Status);
    }

    private static (AssessmentSession Session, InstrumentVersion Version, AssessmentAssignment Assignment) StartedExpiringInAnHour()
    {
        var version = ApprovedVersion();
        var assignment = Ok(AssessmentAssignment.Assign(
            NewHousehold().Members[0], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version),
            AssignmentReason.EligibilityRule, Now.AddHours(1), Now));
        var session = Ok(AssessmentSession.Start(assignment, Now));
        foreach (var item in version.GetItems())
        {
            Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        }

        return (session, version, assignment);
    }

    [Fact]
    public void Submit_is_accepted_until_the_assignment_expires()
    {
        var (session, version, assignment) = StartedExpiringInAnHour();

        Assert.Equal(SubmissionOutcome.Submitted, Ok(session.Submit(version, assignment, Now.AddMinutes(59))));
        Assert.Equal(AssignmentStatus.Completed, assignment.Status);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(60 * 24 * 30)]
    public void Submit_rejects_a_session_whose_assignment_has_passed_its_expiry(int minutesLater)
    {
        var (session, version, assignment) = StartedExpiringInAnHour();

        var refused = session.Submit(version, assignment, Now.AddMinutes(minutesLater));

        Assert.Equal(AssignmentErrors.AssignmentExpired, refused.Error);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Null(session.SubmittedAt);
        Assert.Equal(AssignmentStatus.InProgress, assignment.Status);
        Assert.Empty(session.GetDomainEvents());
    }

    [Fact]
    public void An_expired_assignment_is_reported_before_unanswered_items()
    {
        var version = ApprovedVersion();
        var assignment = Ok(AssessmentAssignment.Assign(
            NewHousehold().Members[0], version, null, OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version),
            AssignmentReason.EligibilityRule, Now.AddHours(1), Now));
        var session = Ok(AssessmentSession.Start(assignment, Now));

        Assert.Equal(AssignmentErrors.AssignmentExpired, session.Submit(version, assignment, Now.AddHours(2)).Error);
    }

    [Fact]
    public void Submitting_again_after_the_expiry_is_still_idempotent()
    {
        var (session, version, assignment) = StartedExpiringInAnHour();
        Ok(session.Submit(version, assignment, Now.AddMinutes(30)));

        Assert.Equal(SubmissionOutcome.AlreadySubmitted, Ok(session.Submit(version, assignment, Now.AddHours(3))));
        Assert.Equal(AssignmentStatus.Completed, assignment.Status);
    }

    // What maintenance does with a session left in progress when its assignment's time has passed.
    [Fact]
    public void An_overdue_session_is_abandoned_and_its_assignment_expired()
    {
        var (session, version, assignment) = StartedExpiringInAnHour();
        var later = Now.AddHours(2);

        Ok(session.Abandon(assignment, later));
        Ok(assignment.Expire(later));

        Assert.Equal((SessionStatus.Abandoned, (DateTimeOffset?)later), (session.Status, session.AbandonedAt));
        Assert.Equal(AssignmentStatus.Expired, assignment.Status);
        Assert.Equal(version.GetItems().Count(), session.Responses.Count);
        Assert.Equal(AssignmentErrors.AssignmentExpired, session.Submit(version, assignment, later).Error);
        Assert.Equal(AssignmentErrors.AssignmentExpired, AssessmentSession.Start(assignment, later).Error);
    }
}
