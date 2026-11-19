using System.Diagnostics.Metrics;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Safeguarding;

public sealed class RecordSafetySignalTests : IDisposable
{
    private const string Subject = "caller-subject";

    private static readonly SafetySignal Crisis = new(
        SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, "suicide.intent", "en");

    private static readonly SafetySignal LowScore = new(
        SafetyAlertSource.HouseholdAssessment, SafetyConcern.LowResilienceScore, SafetyDetector.ScoreBand, "vulnerable", null);

    private readonly SafeguardingStore _store = new();
    private readonly CapturedLogs<RecordSafetySignal> _logs = new();
    private readonly List<(string Instrument, Dictionary<string, string?> Tags)> _measurements = [];
    private readonly ServiceProvider _services;
    private readonly MeterListener _listener = new();

    /// <summary>The caller each of the recorder's own scopes resolves (the recorder never sees the request's).</summary>
    private ICallerAccess _access = new FixedCallerAccess(null);

    public RecordSafetySignalTests()
    {
        _services = new ServiceCollection()
            .AddMetrics()
            .AddSingleton(_store)
            .AddScoped(_ => _access)
            .AddScoped<StoreSafetyAlertRepository>()
            .AddScoped<ISafetyAlertRepository>(provider => provider.GetRequiredService<StoreSafetyAlertRepository>())
            .AddScoped<StoreAuditEventRepository>()
            .AddScoped<IAuditEventRepository>(provider => provider.GetRequiredService<StoreAuditEventRepository>())
            .AddScoped<IUnitOfWork, StoreUnitOfWork>()
            .AddScoped<IAuditLog, AuditLog>()
            .AddSingleton<ICurrentUser>(new FakeCurrentUser(true, Subject, Roles.Participant))
            .AddSingleton<TimeProvider>(new FixedTimeProvider(Now))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var factory = _services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SafeguardingMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var copied = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copied[tag.Key] = tag.Value?.ToString();
            }

            _measurements.Add((instrument.Name, copied));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _services.Dispose();
    }

    /// <summary>A head and a spouse; the caller is the spouse, so the alert must name the spouse's member.</summary>
    private static (CallerRecord Caller, FamilyMember Spouse) SpouseSignsIn()
    {
        var userId = PlatformUserId.New();
        var household = Ok(Household.Enrol(
            HouseholdReference.Generate(), [Head(), Adult(39, relationship: HouseholdRelationship.Spouse)], 1, userId, Now));
        return (new CallerRecord(userId, household), household.Members[1]);
    }

    private RecordSafetySignal Recorder(ICallerAccess access, TimeProvider? clock = null)
    {
        _access = access;
        return new RecordSafetySignal(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new SafeguardingMetrics(_services.GetRequiredService<IMeterFactory>()),
            clock ?? new FixedTimeProvider(Now),
            _logs);
    }

    /// <summary>The failure log line: the fixed code and the exception's type or the refused error's code, never a message.</summary>
    private static string FailureLine(string reason) => $"Safety alert recording failed (safety_alert_failed, {reason})";

    private List<Dictionary<string, string?>> Measured(string instrument) =>
        [.. _measurements.Where(measurement => measurement.Instrument == instrument).Select(measurement => measurement.Tags)];

    [Fact]
    public async Task New_signal_raises_an_alert_and_audits_raised()
    {
        var (caller, spouse) = SpouseSignsIn();

        var outcome = await Recorder(new FixedCallerAccess(caller)).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Raised, outcome);
        var alert = Assert.Single(_store.Alerts);
        Assert.Equal((caller.UserId, caller.Household!.Id, spouse.Id), (alert.UserId, alert.HouseholdId, alert.MemberId));
        Assert.Equal(
            (SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetySeverity.Critical, SafetyDetector.PhraseScreen),
            (alert.Source, alert.Concern, alert.Severity, alert.Detector));
        Assert.Equal(("suicide.intent", "en", (AssessmentSessionId?)null), (alert.SignalCode, alert.Language, alert.AssessmentSessionId));
        Assert.Equal((Now, 1, SafetyAlertStatus.Open), (alert.FirstDetectedAt, alert.OccurrenceCount, alert.Status));
        // The detector acted, not the participant: no trail of crisis signals under their subject outlives erasure.
        var audit = Assert.Single(_store.Audits);
        Assert.Equal(
            ("safeguarding.alert.raised", "safety_alert", alert.Id.Value.ToString(), AuditActorType.System, (string?)null),
            (audit.Action, audit.ResourceType, audit.ResourceId, audit.ActorType, audit.ActorId));
        var raised = Assert.Single(Measured("azwah.safeguarding.alerts_raised"));
        Assert.Equal(
            new Dictionary<string, string?> { ["concern"] = "suicide_or_self_harm", ["severity"] = "critical", ["source"] = "assistant_chat" },
            raised);
        Assert.Empty(Measured("azwah.safeguarding.recording_failures"));
        Assert.Empty(Measured("azwah.safeguarding.alerts_repeated"));
        Assert.Empty(_logs.Lines);
    }

    [Fact]
    public async Task Repeat_for_an_unresolved_alert_recurs_and_audits_repeated()
    {
        var (caller, _) = SpouseSignsIn();
        var recorder = Recorder(new FixedCallerAccess(caller));
        var moderated = Crisis with { Detector = SafetyDetector.AiModeration, SignalCode = null };

        var first = await recorder.RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);
        var second = await recorder.RecordForCallerAsync(moderated, TestContext.Current.CancellationToken);

        Assert.Equal((SafetyAlertOutcome.Raised, SafetyAlertOutcome.Repeated), (first, second));
        var alert = Assert.Single(_store.Alerts);
        Assert.Equal((2, SafetyDetector.AiModeration, (string?)null), (alert.OccurrenceCount, alert.Detector, alert.SignalCode));
        Assert.Equal(
            [("safeguarding.alert.raised", alert.Id.Value.ToString()), ("safeguarding.alert.repeated", alert.Id.Value.ToString())],
            _store.Audits.Select(audit => (audit.Action, audit.ResourceId)));
        Assert.All(_store.Audits, audit => Assert.Equal((AuditActorType.System, (string?)null), (audit.ActorType, audit.ActorId)));
        Assert.Single(Measured("azwah.safeguarding.alerts_raised"));
        var repeated = Assert.Single(Measured("azwah.safeguarding.alerts_repeated"));
        Assert.Equal(
            new Dictionary<string, string?> { ["concern"] = "suicide_or_self_harm", ["severity"] = "critical", ["source"] = "assistant_chat" },
            repeated);
    }

    [Fact]
    public async Task Repeat_of_an_acknowledged_alert_reopens_it_for_staff()
    {
        var (caller, _) = SpouseSignsIn();
        var recorder = Recorder(new FixedCallerAccess(caller));
        await recorder.RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);
        Ok(_store.Alerts[0].Acknowledge("staff-subject", Now));

        var again = await recorder.RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Repeated, again);
        var alert = Assert.Single(_store.Alerts);
        Assert.Equal(
            (SafetyAlertStatus.Open, (string?)null, (DateTimeOffset?)null, 2),
            (alert.Status, alert.AcknowledgedBy, alert.AcknowledgedAt, alert.OccurrenceCount));
        Assert.Single(Measured("azwah.safeguarding.alerts_repeated"));
    }

    [Fact]
    public async Task The_same_concern_from_another_source_raises_its_own_alert()
    {
        var (caller, _) = SpouseSignsIn();
        var recorder = Recorder(new FixedCallerAccess(caller));

        await recorder.RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);
        var fromSearch = await recorder.RecordForCallerAsync(
            Crisis with { Source = SafetyAlertSource.SupportSearch, Language = null }, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Raised, fromSearch);
        Assert.Equal(2, _store.Alerts.Count);
        Assert.Equal(["assistant_chat", "support_search"], Measured("azwah.safeguarding.alerts_raised").Select(tags => tags["source"]));
    }

    [Fact]
    public async Task Caller_without_a_household_raises_an_alert_about_the_user_alone()
    {
        var userId = PlatformUserId.New();

        var outcome = await Recorder(new FixedCallerAccess(new CallerRecord(userId, null)))
            .RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Raised, outcome);
        var alert = Assert.Single(_store.Alerts);
        Assert.Equal((userId, (HouseholdId?)null, (FamilyMemberId?)null), (alert.UserId, alert.HouseholdId, alert.MemberId));
    }

    [Fact]
    public async Task Signal_without_caller_is_skipped()
    {
        var outcome = await Recorder(new FixedCallerAccess(null)).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Skipped, outcome);
        Assert.Empty(_store.Alerts);
        Assert.Empty(_store.Audits);
        Assert.Equal(0, _store.CommitAttempts);
        // A crisis signal from someone the server has no record of is counted, so operators see what went unrecorded.
        var skipped = Assert.Single(_measurements);
        Assert.Equal("azwah.safeguarding.recording_skipped", skipped.Instrument);
        Assert.Equal(new Dictionary<string, string?> { ["source"] = "assistant_chat" }, skipped.Tags);
        Assert.Empty(_logs.Lines);
    }

    [Fact]
    public async Task Session_signal_raises_a_high_alert_for_the_household_and_member()
    {
        var household = NewHousehold();
        var member = household.Members[0];
        var sessionId = AssessmentSessionId.New();

        var outcome = await Recorder(new FixedCallerAccess(null)).RecordForSessionAsync(
            new SafetyAlertSubject(null, household.Id, member.Id), sessionId, LowScore, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Raised, outcome);
        var alert = Assert.Single(_store.Alerts);
        Assert.Equal(((PlatformUserId?)null, household.Id, member.Id, sessionId), (alert.UserId, alert.HouseholdId, alert.MemberId, alert.AssessmentSessionId));
        Assert.Equal((SafetySeverity.High, "vulnerable"), (alert.Severity, alert.SignalCode));
        Assert.Equal("high", Assert.Single(Measured("azwah.safeguarding.alerts_raised"))["severity"]);
    }

    [Fact]
    public async Task Session_signal_for_an_alerted_session_is_skipped()
    {
        var household = NewHousehold();
        var subject = new SafetyAlertSubject(null, household.Id, household.Members[0].Id);
        var sessionId = AssessmentSessionId.New();
        var recorder = Recorder(new FixedCallerAccess(null));
        await recorder.RecordForSessionAsync(subject, sessionId, LowScore, TestContext.Current.CancellationToken);
        Ok(_store.Alerts[0].Resolve(SafetyAlertResolution.FamilyContacted, "staff-subject", Now));

        var again = await recorder.RecordForSessionAsync(subject, sessionId, LowScore, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Skipped, again);
        var alert = Assert.Single(_store.Alerts);
        Assert.Equal(1, alert.OccurrenceCount);
        Assert.Single(_store.Audits);
        Assert.Equal(1, _store.CommitAttempts);
        Assert.Empty(Measured("azwah.safeguarding.recording_skipped"));
    }

    [Fact]
    public async Task Fault_while_saving_returns_failed_and_does_not_throw()
    {
        var (caller, _) = SpouseSignsIn();
        _store.OnCommit = (_, _) => throw new InvalidOperationException($"Synthetic fault naming {caller.UserId.Value}.");

        var outcome = await Recorder(new FixedCallerAccess(caller)).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Failed, outcome);
        Assert.Empty(_store.Alerts);
        Assert.Empty(_store.Audits);
        // Fixed codes only: the exception's type reaches the log, never its message, an ID or text.
        Assert.Equal([FailureLine(nameof(InvalidOperationException))], _logs.Lines);
        Assert.Equal(new Dictionary<string, string?> { ["source"] = "assistant_chat" }, Assert.Single(Measured("azwah.safeguarding.recording_failures")));
        Assert.Empty(Measured("azwah.safeguarding.alerts_raised"));
    }

    [Fact]
    public async Task Cancellation_by_the_caller_propagates()
    {
        var (caller, _) = SpouseSignsIn();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _store.OnCommit = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Result.Success();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Recorder(new FixedCallerAccess(caller)).RecordForCallerAsync(Crisis, cancellation.Token));

        Assert.Empty(_store.Alerts);
        Assert.Empty(_measurements);
        Assert.Empty(_logs.Lines);
    }

    [Fact]
    public async Task Cancellation_the_caller_did_not_ask_for_is_a_failure()
    {
        var (caller, _) = SpouseSignsIn();
        _store.OnCommit = (_, _) => throw new OperationCanceledException("Synthetic database time-out.");

        var outcome = await Recorder(new FixedCallerAccess(caller)).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Failed, outcome);
        Assert.Equal([FailureLine(nameof(OperationCanceledException))], _logs.Lines);
        Assert.Single(Measured("azwah.safeguarding.recording_failures"));
    }

    [Fact]
    public async Task A_database_that_outlasts_the_budget_is_a_failure_logged_and_counted()
    {
        var (caller, _) = SpouseSignsIn();
        var clock = new ManualTimeProvider(Now);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.BeforeRead = async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        var recording = Recorder(new FixedCallerAccess(caller), clock).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(RecordSafetySignal.RecordingLimit - TimeSpan.FromMilliseconds(1));
        var stillWaiting = !recording.IsCompleted;
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var outcome = await recording;

        Assert.True(stillWaiting, "The budget must not end before its limit.");
        Assert.Equal(SafetyAlertOutcome.Failed, outcome);
        Assert.Empty(_store.Alerts);
        Assert.Empty(_store.Audits);
        Assert.Equal([FailureLine(nameof(TaskCanceledException))], _logs.Lines);
        Assert.Equal(new Dictionary<string, string?> { ["source"] = "assistant_chat" }, Assert.Single(Measured("azwah.safeguarding.recording_failures")));
    }

    [Fact]
    public async Task A_caller_lookup_that_outlasts_the_budget_is_a_failure_logged_and_counted()
    {
        var clock = new ManualTimeProvider(Now);
        var lookup = new HangingCallerAccess();

        var recording = Recorder(lookup, clock).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);
        await lookup.Entered.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(RecordSafetySignal.RecordingLimit);
        var outcome = await recording;

        Assert.Equal(SafetyAlertOutcome.Failed, outcome);
        Assert.Equal(0, _store.CommitAttempts);
        Assert.Equal([FailureLine(nameof(TaskCanceledException))], _logs.Lines);
        Assert.Single(Measured("azwah.safeguarding.recording_failures"));
    }

    [Fact]
    public async Task Lost_dedupe_race_recurs_on_the_winning_alert()
    {
        var (caller, spouse) = SpouseSignsIn();
        var winner = SafetyAlert.Raise(
            new SafetyAlertSubject(caller.UserId, caller.Household!.Id, spouse.Id), Crisis.Source, Crisis.Concern,
            SafetyDetector.PhraseScreen, "suicide.wish_to_die", "en", null, Now);
        _store.OnCommit = (attempt, _) =>
        {
            if (attempt > 1)
            {
                return Result.Success();
            }

            // Another request saved its alert first; this insert hit the unique index.
            _store.Alerts.Add(winner);
            return Result.Failure(PersistenceErrors.Conflict);
        };

        var outcome = await Recorder(new FixedCallerAccess(caller)).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Repeated, outcome);
        Assert.Equal(winner, Assert.Single(_store.Alerts));
        Assert.Equal((2, "suicide.intent"), (winner.OccurrenceCount, winner.SignalCode));
        var audit = Assert.Single(_store.Audits);
        Assert.Equal(("safeguarding.alert.repeated", winner.Id.Value.ToString()), (audit.Action, audit.ResourceId));
        Assert.Equal(["azwah.safeguarding.alerts_repeated"], _measurements.Select(measurement => measurement.Instrument));
        Assert.Empty(_logs.Lines);
    }

    [Fact]
    public async Task Concurrent_repeat_is_read_again_and_recurs()
    {
        var (caller, _) = SpouseSignsIn();
        var recorder = Recorder(new FixedCallerAccess(caller));
        await recorder.RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);
        _store.OnCommit = (attempt, _) => attempt == 2 ? Result.Failure(PersistenceErrors.ConcurrencyConflict) : Result.Success();

        var outcome = await recorder.RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Repeated, outcome);
        Assert.Equal(3, _store.CommitAttempts);
        Assert.Equal(["safeguarding.alert.raised", "safeguarding.alert.repeated"], _store.Audits.Select(audit => audit.Action));
        Assert.Empty(Measured("azwah.safeguarding.recording_failures"));
    }

    [Fact]
    public async Task Second_lost_race_returns_failed()
    {
        var (caller, _) = SpouseSignsIn();
        _store.OnCommit = (_, _) => Result.Failure(PersistenceErrors.Conflict);

        var outcome = await Recorder(new FixedCallerAccess(caller)).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Failed, outcome);
        Assert.Equal(2, _store.CommitAttempts);
        Assert.Empty(_store.Audits);
        Assert.Equal([FailureLine("persistence.conflict")], _logs.Lines);
        Assert.Single(Measured("azwah.safeguarding.recording_failures"));
    }

    [Fact]
    public async Task Other_refusals_are_not_retried()
    {
        var (caller, _) = SpouseSignsIn();
        _store.OnCommit = (_, _) => Result.Failure(new Error("audit.action_invalid", "Synthetic refusal."));

        var outcome = await Recorder(new FixedCallerAccess(caller)).RecordForCallerAsync(Crisis, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyAlertOutcome.Failed, outcome);
        Assert.Equal(1, _store.CommitAttempts);
        Assert.Equal([FailureLine("audit.action_invalid")], _logs.Lines);
    }
}
