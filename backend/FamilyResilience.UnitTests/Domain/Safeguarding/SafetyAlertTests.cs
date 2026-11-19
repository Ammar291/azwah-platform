using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Safeguarding;

public sealed class SafetyAlertTests
{
    private static readonly SafetyAlertSubject Subject = new(
        PlatformUserId.From(Guid.CreateVersion7()), HouseholdId.From(Guid.CreateVersion7()), FamilyMemberId.From(Guid.CreateVersion7()));

    private static SafetyAlert Raised(SafetyConcern concern = SafetyConcern.SuicideOrSelfHarm) =>
        SafetyAlert.Raise(Subject, SafetyAlertSource.AssistantChat, concern, SafetyDetector.PhraseScreen, "suicide.intent", "en", null, Now);

    [Theory]
    [InlineData(SafetyConcern.SuicideOrSelfHarm, SafetySeverity.Critical)]
    [InlineData(SafetyConcern.HarmToOthers, SafetySeverity.Critical)]
    [InlineData(SafetyConcern.AbuseOrDanger, SafetySeverity.Critical)]
    [InlineData(SafetyConcern.LowResilienceScore, SafetySeverity.High)]
    public void Raise_sets_severity_from_the_concern_count_one_and_status_open(SafetyConcern concern, SafetySeverity severity)
    {
        var alert = Raised(concern);

        Assert.Equal(severity, alert.Severity);
        Assert.Equal(1, alert.OccurrenceCount);
        Assert.Equal(SafetyAlertStatus.Open, alert.Status);
        Assert.Equal((Now, Now), (alert.FirstDetectedAt, alert.LastDetectedAt));
        Assert.Equal((Subject.UserId, Subject.HouseholdId, Subject.MemberId), (alert.UserId, alert.HouseholdId, alert.MemberId));
        Assert.Equal(("suicide.intent", "en"), (alert.SignalCode, alert.Language));
    }

    [Fact]
    public void Raise_keeps_the_assessment_session_of_a_score_alert()
    {
        var session = AssessmentSessionId.From(Guid.CreateVersion7());

        var alert = SafetyAlert.Raise(
            Subject, SafetyAlertSource.HouseholdAssessment, SafetyConcern.LowResilienceScore, SafetyDetector.ScoreBand, "vulnerable", null, session, Now);

        Assert.Equal(session, alert.AssessmentSessionId);
        Assert.Null(alert.Language);
    }

    [Fact]
    public void Recur_increments_the_count_and_moves_the_last_time_detector_and_code()
    {
        var alert = Raised();

        Ok(alert.Recur(SafetyDetector.AiModeration, null, Now.AddMinutes(5)));

        Assert.Equal(2, alert.OccurrenceCount);
        Assert.Equal(Now.AddMinutes(5), alert.LastDetectedAt);
        Assert.Equal(Now, alert.FirstDetectedAt);
        Assert.Equal(SafetyDetector.AiModeration, alert.Detector);
        Assert.Null(alert.SignalCode);
    }

    [Fact]
    public void Acknowledge_works_from_open_only()
    {
        var alert = Raised();

        Ok(alert.Acknowledge("staff-1", Now.AddHours(1)));

        Assert.Equal(SafetyAlertStatus.Acknowledged, alert.Status);
        Assert.Equal(("staff-1", (DateTimeOffset?)Now.AddHours(1)), (alert.AcknowledgedBy, alert.AcknowledgedAt));
        Assert.Equal(SafetyErrors.AlertNotOpen, alert.Acknowledge("staff-2", Now.AddHours(2)).Error);
    }

    [Fact]
    public void Acknowledge_fails_once_resolved()
    {
        var alert = Raised();
        Ok(alert.Resolve(SafetyAlertResolution.FalseAlarm, "staff-1", Now.AddHours(1)));

        Assert.Equal(SafetyErrors.AlertNotOpen, alert.Acknowledge("staff-2", Now.AddHours(2)).Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_works_from_open_or_acknowledged_and_fails_twice(bool acknowledgeFirst)
    {
        var alert = Raised();
        if (acknowledgeFirst)
        {
            Ok(alert.Acknowledge("staff-1", Now.AddHours(1)));
        }

        Ok(alert.Resolve(SafetyAlertResolution.FamilyContacted, "staff-2", Now.AddHours(2)));

        Assert.Equal(SafetyAlertStatus.Resolved, alert.Status);
        Assert.Equal(SafetyAlertResolution.FamilyContacted, alert.Resolution);
        Assert.Equal(("staff-2", (DateTimeOffset?)Now.AddHours(2)), (alert.ResolvedBy, alert.ResolvedAt));
        Assert.Equal(SafetyErrors.AlertResolved, alert.Resolve(SafetyAlertResolution.NoActionNeeded, "staff-3", Now.AddHours(3)).Error);
    }

    [Fact]
    public void Recur_reopens_an_acknowledged_alert_and_clears_the_acknowledgement()
    {
        // A repeat after staff acknowledged the alert must page again: it goes back to the open queue.
        var alert = Raised();
        Ok(alert.Acknowledge("staff-1", Now.AddHours(1)));

        Ok(alert.Recur(SafetyDetector.AiModeration, null, Now.AddHours(2)));

        Assert.Equal(
            (SafetyAlertStatus.Open, (string?)null, (DateTimeOffset?)null, 2, Now.AddHours(2)),
            (alert.Status, alert.AcknowledgedBy, alert.AcknowledgedAt, alert.OccurrenceCount, alert.LastDetectedAt));
        Ok(alert.Acknowledge("staff-2", Now.AddHours(3)));
        Assert.Equal(("staff-2", (DateTimeOffset?)Now.AddHours(3)), (alert.AcknowledgedBy, alert.AcknowledgedAt));
    }

    [Fact]
    public void Recur_keeps_an_open_alert_open()
    {
        var alert = Raised();

        Ok(alert.Recur(SafetyDetector.PhraseScreen, "suicide.intent", Now.AddMinutes(1)));

        Assert.Equal((SafetyAlertStatus.Open, (string?)null), (alert.Status, alert.AcknowledgedBy));
    }

    [Fact]
    public void Recur_after_resolve_fails_and_changes_nothing()
    {
        var alert = Raised();
        Ok(alert.Resolve(SafetyAlertResolution.NoActionNeeded, "staff-1", Now.AddHours(1)));

        var result = alert.Recur(SafetyDetector.PhraseScreen, "suicide.intent", Now.AddHours(2));

        Assert.Equal(SafetyErrors.AlertResolved, result.Error);
        Assert.Equal(1, alert.OccurrenceCount);
        Assert.Equal(Now, alert.LastDetectedAt);
    }

    [Fact]
    public void A_subject_without_user_and_household_throws()
    {
        Assert.Throws<ArgumentException>(() => new SafetyAlertSubject(null, null, FamilyMemberId.From(Guid.CreateVersion7())));
    }

    [Fact]
    public void A_subject_with_a_member_but_no_household_throws()
    {
        var thrown = Assert.Throws<ArgumentException>(() => new SafetyAlertSubject(
            PlatformUserId.From(Guid.CreateVersion7()), null, FamilyMemberId.From(Guid.CreateVersion7())));

        Assert.Equal("householdId", thrown.ParamName);
    }

    [Fact]
    public void A_subject_with_only_a_household_is_allowed()
    {
        var subject = new SafetyAlertSubject(null, HouseholdId.From(Guid.CreateVersion7()), null);

        Assert.Null(subject.UserId);
    }

    [Theory]
    [InlineData("Suicide.Intent")]
    [InlineData("suicide..intent")]
    [InlineData("1suicide")]
    [InlineData("suicide intent")]
    [InlineData("")]
    public void A_bad_signal_code_throws(string code)
    {
        Assert.Throws<ArgumentException>(() => SafetyAlert.Raise(
            Subject, SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, code, "en", null, Now));
        Assert.Throws<ArgumentException>(() => Raised().Recur(SafetyDetector.PhraseScreen, code, Now));
    }

    [Fact]
    public void A_signal_code_longer_than_sixty_characters_throws()
    {
        var code = new string('a', 61);

        Assert.Throws<ArgumentException>(() => SafetyAlert.Raise(
            Subject, SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, code, "en", null, Now));
    }

    [Fact]
    public void An_unknown_language_throws()
    {
        Assert.Throws<ArgumentException>(() => SafetyAlert.Raise(
            Subject, SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, null, "fr", null, Now));
    }

    [Fact]
    public void A_time_that_is_not_utc_throws()
    {
        Assert.Throws<ArgumentException>(() => SafetyAlert.Raise(
            Subject, SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, null, "en", null, Now.ToOffset(TimeSpan.FromHours(4))));
    }
}
