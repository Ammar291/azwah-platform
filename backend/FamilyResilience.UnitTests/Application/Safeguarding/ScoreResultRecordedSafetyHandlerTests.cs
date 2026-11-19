using System.Diagnostics.Metrics;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using FamilyResilience.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Safeguarding;

public sealed class ScoreResultRecordedSafetyHandlerTests
{
    private const string RuleSet = "household_assessment_total";

    private static readonly ScoreAlertSettings Settings = new() { Tiers = { [RuleSet] = ["vulnerable"] } };

    private static readonly SafetySignal LowScore = new(
        SafetyAlertSource.HouseholdAssessment, SafetyConcern.LowResilienceScore, SafetyDetector.ScoreBand, "vulnerable", null);

    private readonly FakeScoreResultFacts _facts = new();

    /// <summary>
    /// A result as the Scoring module describes it, known to the fake reader, and its outbox message exactly as the
    /// serializer writes it.
    /// </summary>
    private (ScoreResultId Id, ScoreResultFacts Facts, OutboxEnvelope Message) Recorded(
        string ruleSet = RuleSet, ScoreStatus status = ScoreStatus.Complete, string? tier = "vulnerable")
    {
        var id = ScoreResultId.New();
        var facts = new ScoreResultFacts(ruleSet, status, tier, AssessmentSessionId.New(), HouseholdId.New(), FamilyMemberId.New());
        _facts.Results[id] = facts;
        return (id, facts, Envelope(id, facts.SessionId));
    }

    private static OutboxEnvelope Envelope(ScoreResultId id, AssessmentSessionId sessionId)
    {
        var message = DomainEventSerializer.ToOutboxMessage(new ScoreResultRecorded(id, sessionId, RuleSetId.New(), Now));
        return new OutboxEnvelope(message.Id, message.Type, message.Payload, message.OccurredAt);
    }

    private ScoreResultRecordedSafetyHandler Handler(ISafetySignalRecorder recorder) => new(_facts, Settings, recorder);

    [Fact]
    public async Task Vulnerable_tier_raises_low_score_alert()
    {
        var (id, facts, message) = Recorded();
        var recorder = new SessionRecorder();
        var handler = Handler(recorder);

        await handler.HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal("scoring.score_result_recorded.v1", handler.Type);
        Assert.Equal(handler.Type, message.Type);
        Assert.Equal([id], _facts.Requested);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal(new SafetyAlertSubject(null, facts.HouseholdId, facts.MemberId), call.Subject);
        Assert.Equal(facts.SessionId, call.SessionId);
        Assert.Equal(LowScore, call.Signal);
    }

    [Theory]
    [InlineData("transitional")]
    [InlineData("flourishing")]
    [InlineData(null)]
    public async Task Other_tier_raises_nothing(string? tier)
    {
        var (_, _, message) = Recorded(tier: tier);
        var recorder = new SessionRecorder();

        await Handler(recorder).HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task Incomplete_result_raises_nothing()
    {
        // The domain never records a tier on an incomplete result; the status alone must still keep the alert back.
        var (_, _, message) = Recorded(status: ScoreStatus.Incomplete, tier: "vulnerable");
        var recorder = new SessionRecorder();

        await Handler(recorder).HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task Unlisted_policy_raises_nothing()
    {
        // Another scoring rule set, even one whose bands use the same tier code.
        var (_, _, message) = Recorded(ruleSet: "family_voices_total");
        var recorder = new SessionRecorder();

        await Handler(recorder).HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task No_configured_tiers_raise_nothing()
    {
        var (_, _, message) = Recorded();
        var recorder = new SessionRecorder();

        await new ScoreResultRecordedSafetyHandler(_facts, new ScoreAlertSettings(), recorder)
            .HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task Disabled_trigger_raises_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, _, message) = Recorded();
        var malformed = new OutboxEnvelope(Guid.NewGuid(), ScoreResultRecordedSafetyHandler.MessageType, "not json", Now);
        var recorder = new SessionRecorder();
        var handler = new ScoreResultRecordedSafetyHandler(
            _facts, new ScoreAlertSettings(enabled: "false") { Tiers = { [RuleSet] = ["vulnerable"] } }, recorder);

        // Switched off, every message completes: none is read, recorded or retried.
        await handler.HandleAsync(message, cancellationToken);
        await handler.HandleAsync(malformed, cancellationToken);

        Assert.Empty(_facts.Requested);
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task A_result_that_no_longer_exists_raises_nothing()
    {
        // For example, the household was erased before the message was delivered.
        var message = Envelope(ScoreResultId.New(), AssessmentSessionId.New());
        var recorder = new SessionRecorder();

        await Handler(recorder).HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task Second_delivery_is_skipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, facts, message) = Recorded();
        var store = new SafeguardingStore();
        await using var services = RecorderServices(store);
        var recorder = new OutcomeRecorder(new RecordSafetySignal(
            services.GetRequiredService<IServiceScopeFactory>(),
            new SafeguardingMetrics(services.GetRequiredService<IMeterFactory>()),
            new FixedTimeProvider(Now),
            new CapturedLogs<RecordSafetySignal>()));
        var handler = Handler(recorder);

        await handler.HandleAsync(message, cancellationToken);
        await handler.HandleAsync(message, cancellationToken);

        Assert.Equal([SafetyAlertOutcome.Raised, SafetyAlertOutcome.Skipped], recorder.Outcomes);
        var alert = Assert.Single(store.Alerts);
        Assert.Null(alert.UserId);
        Assert.Equal(
            ((HouseholdId?)facts.HouseholdId, (FamilyMemberId?)facts.MemberId, (AssessmentSessionId?)facts.SessionId),
            (alert.HouseholdId, alert.MemberId, alert.AssessmentSessionId));
        Assert.Equal(
            (SafetyConcern.LowResilienceScore, SafetySeverity.High, SafetyDetector.ScoreBand, "vulnerable"),
            (alert.Concern, alert.Severity, alert.Detector, alert.SignalCode));
        Assert.Equal("safeguarding.alert.raised", Assert.Single(store.Audits).Action);
    }

    [Theory]
    [InlineData(SafetyAlertOutcome.Raised)]
    [InlineData(SafetyAlertOutcome.Skipped)]
    [InlineData(SafetyAlertOutcome.Repeated)]
    public async Task Every_outcome_but_failed_completes_the_message(SafetyAlertOutcome outcome)
    {
        var (_, _, message) = Recorded();
        var recorder = new SessionRecorder(outcome);

        await Handler(recorder).HandleAsync(message, TestContext.Current.CancellationToken);

        Assert.Single(recorder.Calls);
    }

    [Fact]
    public async Task Failed_recording_throws_so_the_outbox_retries()
    {
        var (id, facts, message) = Recorded();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(new SessionRecorder(SafetyAlertOutcome.Failed)).HandleAsync(message, TestContext.Current.CancellationToken));

        foreach (var identifier in new[] { id.Value, facts.SessionId.Value, facts.HouseholdId.Value, facts.MemberId.Value, message.Id })
        {
            Assert.DoesNotContain(identifier.ToString(), error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"scoreResultId":"not-a-uuid"}""")]
    [InlineData("""{"scoreResultId":42}""")]
    public async Task A_malformed_payload_throws_so_the_outbox_retries(string payload)
    {
        var recorder = new SessionRecorder();
        var message = new OutboxEnvelope(Guid.NewGuid(), ScoreResultRecordedSafetyHandler.MessageType, payload, Now);

        await Assert.ThrowsAnyAsync<Exception>(() => Handler(recorder).HandleAsync(message, TestContext.Current.CancellationToken));

        Assert.Empty(_facts.Requested);
        Assert.Empty(recorder.Calls);
    }

    /// <summary>What the real recorder needs, over the in-memory store; the outbox runs as the platform, not a user.</summary>
    private static ServiceProvider RecorderServices(SafeguardingStore store) =>
        new ServiceCollection()
            .AddMetrics()
            .AddSingleton(store)
            .AddScoped<StoreSafetyAlertRepository>()
            .AddScoped<ISafetyAlertRepository>(provider => provider.GetRequiredService<StoreSafetyAlertRepository>())
            .AddScoped<StoreAuditEventRepository>()
            .AddScoped<IAuditEventRepository>(provider => provider.GetRequiredService<StoreAuditEventRepository>())
            .AddScoped<IUnitOfWork, StoreUnitOfWork>()
            .AddScoped<IAuditLog, AuditLog>()
            .AddSingleton<ICurrentUser>(new FakeCurrentUser(false, null) { IsSystem = true })
            .AddSingleton<TimeProvider>(new FixedTimeProvider(Now))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    /// <summary>Score results by ID, as the Scoring module would read them; remembers which IDs were asked for.</summary>
    private sealed class FakeScoreResultFacts : IScoreResultFacts
    {
        public Dictionary<ScoreResultId, ScoreResultFacts> Results { get; } = [];

        public List<ScoreResultId> Requested { get; } = [];

        public Task<ScoreResultFacts?> GetAsync(ScoreResultId id, CancellationToken cancellationToken = default)
        {
            Requested.Add(id);
            return Task.FromResult(Results.GetValueOrDefault(id));
        }
    }

    private sealed record SessionCall(SafetyAlertSubject Subject, AssessmentSessionId SessionId, SafetySignal Signal);

    /// <summary>Keeps every session signal it is given and answers <paramref name="outcome"/>.</summary>
    private sealed class SessionRecorder(SafetyAlertOutcome outcome = SafetyAlertOutcome.Raised) : ISafetySignalRecorder
    {
        public List<SessionCall> Calls { get; } = [];

        public Task<SafetyAlertOutcome> RecordForCallerAsync(SafetySignal signal, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A score signal is about a session, not the caller.");

        public Task<SafetyAlertOutcome> RecordForSessionAsync(
            SafetyAlertSubject subject, AssessmentSessionId sessionId, SafetySignal signal, CancellationToken cancellationToken)
        {
            Calls.Add(new SessionCall(subject, sessionId, signal));
            return Task.FromResult(outcome);
        }
    }

    /// <summary>The real recorder, with each outcome it returned kept for the test.</summary>
    private sealed class OutcomeRecorder(ISafetySignalRecorder inner) : ISafetySignalRecorder
    {
        public List<SafetyAlertOutcome> Outcomes { get; } = [];

        public Task<SafetyAlertOutcome> RecordForCallerAsync(SafetySignal signal, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A score signal is about a session, not the caller.");

        public async Task<SafetyAlertOutcome> RecordForSessionAsync(
            SafetyAlertSubject subject, AssessmentSessionId sessionId, SafetySignal signal, CancellationToken cancellationToken)
        {
            var outcome = await inner.RecordForSessionAsync(subject, sessionId, signal, cancellationToken);
            Outcomes.Add(outcome);
            return outcome;
        }
    }
}
