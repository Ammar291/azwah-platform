using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.UnitTests.Infrastructure;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Assistant;

public sealed class StartConsultantSessionTests
{
    private static readonly AssistantLimits ShortLimits = AssistantLimits.Default with { ModelTimeout = TimeSpan.FromMilliseconds(200) };

    private readonly FakeAuditEventRepository _audits = new();
    private readonly LiveConsultantSessions _live = new();

    private static ConsultantStartRequest Arabic() => new() { Language = AssistantLanguages.Arabic };

    private static readonly ConsultantStartMessage Fresh = new(ConsultantStartMessage.TypeName, null, null);

    private readonly RecordingToolRunner _tools = new();

    private StartConsultantSession Create(
        IVoiceConsultantModel model, SyntheticFamily family, string subject = SyntheticFamily.Subject, IAssistantSafetyCheck? safety = null)
    {
        var access = new CallerAccess(new FakeCurrentUser(true, subject), new FixedCallerQueries(new CallerRecord(family.CallerId, family.Household)));
        var auditLog = new AuditLog(_audits, new FakeUnitOfWork(), new FakeCurrentUser(true, subject), new FixedTimeProvider(Now));
        var screen = new ConsultantHistoryScreen(
            new CrisisSignalScreen(CrisisSignalCatalog.Load()), safety ?? new NoAssistantSafetyCheck(), ShortLimits);
        return new StartConsultantSession(
            model, family.Builder(), _live, new FakeCurrentUser(true, subject), access, auditLog, _tools, ShortLimits, screen,
            new NoConsultantCrisisReply(), TestAssistantMetrics.Create(), new CapturedLogs<ContinuingConsultantSession>());
    }

    [Fact]
    public async Task Without_a_model_it_is_unavailable_and_claims_builds_and_audits_nothing()
    {
        var model = new ScriptedVoiceModel { IsConfigured = false };

        var result = await Create(model, SyntheticFamily.HeadSignsIn()).HandleAsync(Arabic(), Fresh, TestContext.Current.CancellationToken);

        Assert.Equal(AssistantErrors.Unavailable, result.Error);
        Assert.Empty(model.Requests);
        Assert.Empty(_audits.Added);
        Assert.Equal(0, _live.Count);
    }

    [Fact]
    public async Task A_start_opens_the_model_with_the_context_holds_the_slot_and_is_audited()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var opened = new SilentVoiceSession();
        var model = new ScriptedVoiceModel { Open = _ => Task.FromResult<IVoiceConsultantSession>(opened) };

        var result = await Create(model, family).HandleAsync(Arabic(), Fresh, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(model.Requests);
        Assert.Equal(AssistantLanguages.Arabic, request.Language);
        Assert.Equal((ConsultantOpening.Greet, (string?)null), (request.Opening, request.Note));
        Assert.NotNull(request.Context);
        Assert.Equal(AssistantLanguages.Arabic, request.Context.Language);
        Assert.IsType<ContinuingConsultantSession>(result.Value.Session);
        Assert.Equal(1, _live.Count);
        var audit = Assert.Single(_audits.Added);
        Assert.Equal(
            (StartConsultantSession.AuditAction, "household", family.Household.Id.Value.ToString(), AuditOutcome.Succeeded),
            (audit.Action, audit.ResourceType, audit.ResourceId, audit.Outcome));

        await result.Value.DisposeAsync();
        Assert.True(opened.Disposed);
        Assert.Equal(0, _live.Count);
    }

    [Fact]
    public async Task A_start_carries_the_trimmed_memory_the_earlier_messages_and_the_tools_to_the_model()
    {
        var model = new ScriptedVoiceModel();
        var start = new ConsultantStartMessage(
            ConsultantStartMessage.TypeName,
            "  - Synthetic memory ",
            [
                new AssistantMessageRequest(AssistantMessageRole.User, " Typed earlier "),
                new AssistantMessageRequest(AssistantMessageRole.Assistant, "Said on a call", AssistantMessageChannel.Voice),
            ]);

        var result = await Create(model, SyntheticFamily.HeadSignsIn()).HandleAsync(Arabic(), start, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(model.Requests);
        Assert.Equal("- Synthetic memory", request.Memory);
        Assert.Equal(
            [
                new AssistantMessage(AssistantMessageRole.User, "Typed earlier"),
                new AssistantMessage(AssistantMessageRole.Assistant, "Said on a call", AssistantMessageChannel.Voice),
            ],
            request.History);
        Assert.Same(_tools, request.Tools);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task Earlier_lines_that_would_trip_the_filter_are_held_back_and_replaced_by_the_note()
    {
        var model = new ScriptedVoiceModel();
        var safety = new FixedSafetyCheck(AssistantSafetyVerdict.Clear);
        var start = new ConsultantStartMessage(
            ConsultantStartMessage.TypeName,
            "- Synthetic memory",
            [
                new AssistantMessageRequest(AssistantMessageRole.User, "My son argues about his phone"),
                new AssistantMessageRequest(AssistantMessageRole.User, "I want to end my life", AssistantMessageChannel.Voice),
                new AssistantMessageRequest(AssistantMessageRole.Assistant, "Synthetic support", AssistantMessageChannel.Voice),
            ]);

        var result = await Create(model, SyntheticFamily.HeadSignsIn(), safety: safety).HandleAsync(Arabic(), start, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(model.Requests);
        Assert.Equal(
            [
                new AssistantMessage(AssistantMessageRole.User, "My son argues about his phone"),
                new AssistantMessage(AssistantMessageRole.Assistant, "Synthetic support", AssistantMessageChannel.Voice),
            ],
            request.History);
        Assert.Equal((AssistantPrompt.WithheldHistoryNote, "- Synthetic memory"), (request.Note, request.Memory));
        Assert.Equal(["My son argues about his phone", "Synthetic support"], safety.Checked);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task The_model_may_connect_before_the_context_and_the_screened_history_are_ready()
    {
        var model = new EarlyVoiceModel();
        var slowCheck = new FixedSafetyCheck(AssistantSafetyVerdict.Clear, TimeSpan.FromMilliseconds(50));
        var start = new ConsultantStartMessage(
            ConsultantStartMessage.TypeName, null, [new AssistantMessageRequest(AssistantMessageRole.User, "Synthetic earlier line")]);

        var result = await Create(model, SyntheticFamily.HeadSignsIn(), safety: slowCheck).HandleAsync(Arabic(), start, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(model.CalledBeforeReady);
        Assert.NotNull(model.Prepared?.Context);
        await result.Value.DisposeAsync();
    }

    /// <summary>A model that records whether it was asked to open before its request was ready, as the OpenAI adapter connects early.</summary>
    private sealed class EarlyVoiceModel : IVoiceConsultantModel
    {
        public bool IsConfigured => true;

        public bool CalledBeforeReady { get; private set; }

        public VoiceConsultantRequest? Prepared { get; private set; }

        public Task<IVoiceConsultantSession> OpenAsync(VoiceConsultantRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The early path is expected.");

        public async Task<IVoiceConsultantSession> OpenAsync(Task<VoiceConsultantRequest> request, CancellationToken cancellationToken)
        {
            CalledBeforeReady = !request.IsCompleted;
            Prepared = await request;
            return new SilentVoiceSession();
        }
    }

    [Fact]
    public async Task A_second_start_by_the_same_subject_replaces_the_first()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = await Create(new ScriptedVoiceModel(), family).HandleAsync(Arabic(), Fresh, cancellationToken);
        var other = await Create(new ScriptedVoiceModel(), family, "another-subject").HandleAsync(Arabic(), Fresh, cancellationToken);
        var second = await Create(new ScriptedVoiceModel(), family).HandleAsync(Arabic(), Fresh, cancellationToken);

        Assert.True(first.Value.Replaced.IsCancellationRequested);
        Assert.False(other.Value.Replaced.IsCancellationRequested);
        Assert.False(second.Value.Replaced.IsCancellationRequested);
        await first.Value.DisposeAsync();
        Assert.Equal(2, _live.Count);
        await second.Value.DisposeAsync();
        await other.Value.DisposeAsync();
        Assert.Equal(0, _live.Count);
    }

    [Fact]
    public async Task A_model_that_fails_to_open_frees_the_slot_and_is_audited_as_failed()
    {
        var model = new ScriptedVoiceModel
        {
            Open = _ => Task.FromException<IVoiceConsultantSession>(new InvalidOperationException("Synthetic adapter fault.")),
        };

        var result = await Create(model, SyntheticFamily.HeadSignsIn()).HandleAsync(Arabic(), Fresh, TestContext.Current.CancellationToken);

        Assert.Equal(AssistantErrors.ModelFailed, result.Error);
        Assert.Equal(0, _live.Count);
        Assert.Equal(AuditOutcome.Failed, Assert.Single(_audits.Added).Outcome);
    }

    [Fact]
    public async Task A_model_that_opens_too_late_is_cut_off_and_its_session_closed_when_it_opens()
    {
        var late = new TaskCompletionSource<IVoiceConsultantSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedVoiceModel { Open = _ => late.Task };

        var result = await Create(model, SyntheticFamily.HeadSignsIn()).HandleAsync(Arabic(), Fresh, TestContext.Current.CancellationToken);
        var session = new SilentVoiceSession();
        late.SetResult(session);

        Assert.Equal(AssistantErrors.ModelFailed, result.Error);
        Assert.Equal(0, _live.Count);
        for (var attempt = 0; attempt < 50 && !session.Disposed; attempt++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(session.Disposed);
    }
}
