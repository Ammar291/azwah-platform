using System.Diagnostics;
using System.Diagnostics.Metrics;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.UnitTests.Application.Safeguarding;
using FamilyResilience.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Assistant;

public sealed class GenerateAssistantReplyTests
{
    private const string CrisisMessage = "I want to end my life";

    private static readonly AssistantLimits ShortLimits = AssistantLimits.Default with { ModelTimeout = TimeSpan.FromMilliseconds(200) };

    private static readonly CrisisSignalScreen Screen = new(CrisisSignalCatalog.Load());

    private readonly FakeAuditEventRepository _audits = new();

    private readonly CapturedLogs<GenerateAssistantReply> _logs = new();

    private RecordingSafetySignalRecorder _recorder = new();

    /// <summary>An audit log whose database is down: recording throws.</summary>
    private sealed class FailingAuditLog : IAuditLog
    {
        public Result Stage(AuditRecord record) => throw new InvalidOperationException("Synthetic audit fault.");

        public Task<Result> RecordAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic audit fault.");
    }

    private static AssistantReplyRequest Request(params (AssistantMessageRole Role, string Text)[] messages) =>
        new(AssistantLanguages.English, messages.Select(message => new AssistantMessageRequest(message.Role, message.Text)).ToList());

    private (GenerateAssistantReply UseCase, FixedCallerQueries Queries) Create(
        IAssistantModel model,
        SyntheticFamily family,
        AssistantLimits? limits = null,
        IAssistantToolRunner? tools = null,
        IAssistantSafetyCheck? safety = null,
        ISafetySignalRecorder? recorder = null)
    {
        var queries = new FixedCallerQueries(new CallerRecord(family.CallerId, family.Household));
        var access = new CallerAccess(new FakeCurrentUser(true, SyntheticFamily.Subject), queries);
        return (UseCase(model, family.Builder(), access, limits, tools, safety, recorder), queries);
    }

    private GenerateAssistantReply UseCase(
        IAssistantModel model,
        FamilyContextBuilder builder,
        CallerAccess access,
        AssistantLimits? limits = null,
        IAssistantToolRunner? tools = null,
        IAssistantSafetyCheck? safety = null,
        ISafetySignalRecorder? recorder = null,
        IAuditLog? auditLog = null)
    {
        auditLog ??= new AuditLog(_audits, new FakeUnitOfWork(), new FakeCurrentUser(true, SyntheticFamily.Subject), new FixedTimeProvider(Now));
        var metrics = TestAssistantMetrics.Create();
        var agent = new AssistantAgent(model, tools ?? new RecordingToolRunner(), safety ?? new NoAssistantSafetyCheck(), metrics);
        return new GenerateAssistantReply(
            model, agent, builder, access, Screen, recorder ?? _recorder, auditLog, limits ?? ShortLimits, metrics, new FixedTimeProvider(Now), _logs);
    }

    /// <summary>The family's caller, with the family context built by <paramref name="builder"/>.</summary>
    private GenerateAssistantReply UseCaseOver(
        IAssistantModel model, SyntheticFamily family, FamilyContextBuilder builder, IAssistantSafetyCheck? safety = null, IAuditLog? auditLog = null) =>
        UseCase(
            model,
            builder,
            new CallerAccess(new FakeCurrentUser(true, SyntheticFamily.Subject), new FixedCallerQueries(new CallerRecord(family.CallerId, family.Household))),
            safety: safety,
            auditLog: auditLog);

    [Fact]
    public async Task A_context_build_that_throws_after_a_crisis_message_still_answers_with_the_notice()
    {
        // The notice must never depend on the database (D-138): a fault after the phrase matched is a failure carrying it.
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("unused"));
        var useCase = UseCaseOver(model, family, family.BuilderOver(new FailingCallerQueries()));

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), TestContext.Current.CancellationToken);

        Assert.Equal((AssistantErrors.Faulted, SafetyNotices.CrisisSupport), (outcome.Result.Error, outcome.SafetyNotice));
        Assert.Equal(SafetyDetector.PhraseScreen, Assert.Single(_recorder.Signals).Detector);
        Assert.Empty(model.Steps);
        // Only the exception's type is logged, never its message.
        Assert.Equal(["An assistant reply stopped on a fault of type InvalidOperationException after a crisis signal."], _logs.Lines);
    }

    [Fact]
    public async Task An_audit_that_throws_after_a_crisis_message_still_answers_with_the_notice()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var useCase = UseCaseOver(
            new ScriptedAssistantModel(ScriptedAssistantModel.Reply("I am here for you.")), family, family.Builder(), auditLog: new FailingAuditLog());

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), TestContext.Current.CancellationToken);

        Assert.Equal((AssistantErrors.Faulted, SafetyNotices.CrisisSupport), (outcome.Result.Error, outcome.SafetyNotice));
        Assert.Single(_recorder.Calls);
    }

    [Fact]
    public async Task An_audit_that_throws_after_a_moderation_flag_still_answers_with_the_notice()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var useCase = UseCaseOver(
            new ScriptedAssistantModel(ScriptedAssistantModel.Reply("I am here for you.")),
            family,
            family.Builder(),
            safety: new FixedSafetyCheck(AssistantSafetyVerdict.DangerOfViolence),
            auditLog: new FailingAuditLog());

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, "zqx words the list does not hold")), TestContext.Current.CancellationToken);

        Assert.Equal((AssistantErrors.Faulted, SafetyNotices.CrisisSupport), (outcome.Result.Error, outcome.SafetyNotice));
        Assert.Equal(SafetyDetector.AiModeration, Assert.Single(_recorder.Signals).Detector);
    }

    [Fact]
    public async Task A_fault_without_a_crisis_signal_still_propagates()
    {
        // Without a signal there is no notice to carry, and the global handler answers the fault as before.
        var family = SyntheticFamily.HeadSignsIn();
        var useCase = UseCaseOver(
            new ScriptedAssistantModel(ScriptedAssistantModel.Reply("unused")), family, family.BuilderOver(new FailingCallerQueries()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.HandleAsync(Request((AssistantMessageRole.User, "Hello")), TestContext.Current.CancellationToken));

        Assert.Empty(_logs.Lines);
    }

    [Fact]
    public async Task The_model_starts_while_the_phrase_recording_is_still_running_and_the_reply_waits_for_it()
    {
        // A slow database must not hold the model back by up to the recording budget.
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _recorder = new RecordingSafetySignalRecorder { Hold = hold.Task };
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ScriptedAssistantModel((_, _) =>
        {
            asked.TrySetResult();
            return Task.FromResult(Result.Success(AssistantModelTurn.Reply("I am here for you.")));
        });
        var (useCase, _) = Create(model, SyntheticFamily.HeadSignsIn(), AssistantLimits.Default);

        var replying = useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), TestContext.Current.CancellationToken);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var answeredBeforeTheRecording = replying.IsCompleted;
        hold.SetResult();
        var outcome = await replying;

        Assert.False(answeredBeforeTheRecording, "The reply must wait for the recording before it returns.");
        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal(SafetyNotices.CrisisSupport, outcome.SafetyNotice);
        Assert.False(Assert.Single(_recorder.Calls).Token.CanBeCanceled);
    }

    [Fact]
    public async Task Without_a_model_it_is_unavailable_and_builds_sends_and_audits_nothing()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("unused")) { IsConfigured = false };
        var (useCase, queries) = Create(model, family);

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, "Hello")), TestContext.Current.CancellationToken);

        Assert.Equal((AssistantErrors.Unavailable, (string?)null), (outcome.Result.Error, outcome.SafetyNotice));
        Assert.Empty(model.Steps);
        Assert.Empty(_audits.Added);
        Assert.Empty(_recorder.Calls);
        Assert.Equal(0, queries.Lookups);
    }

    [Fact]
    public async Task A_reply_carries_the_context_the_memory_and_the_trimmed_chat_and_is_audited_without_text()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("  A **synthetic** reply.  "));
        var (useCase, _) = Create(model, family);
        var request = new AssistantReplyRequest(
            AssistantLanguages.English,
            [
                new AssistantMessageRequest(AssistantMessageRole.User, " First "),
                new AssistantMessageRequest(AssistantMessageRole.Assistant, "Said on a call", AssistantMessageChannel.Voice),
                new AssistantMessageRequest(AssistantMessageRole.User, "  Second\n"),
            ],
            "  - Synthetic earlier memory ");

        var outcome = await useCase.HandleAsync(request, TestContext.Current.CancellationToken);

        var result = outcome.Result;
        Assert.True(result.IsSuccess);
        Assert.Equal(("A synthetic reply.", Now, (string?)null), (result.Value.Reply, result.Value.GeneratedAt, result.Value.Memory));
        Assert.Empty(result.Value.Actions);
        Assert.Empty(result.Value.Sources);
        Assert.Null(result.Value.SafetyNotice);
        Assert.Null(outcome.SafetyNotice);
        Assert.Empty(_recorder.Calls);
        var step = Assert.Single(model.Steps);
        Assert.Equal(AssistantPrompt.ChatInstructions(AssistantLanguages.English), step.Instructions);
        Assert.Collection(
            step.Input,
            context => Assert.StartsWith(AssistantPrompt.ContextHeading, context.Text, StringComparison.Ordinal),
            memory => Assert.Equal($"{AssistantPrompt.MemoryHeading}\n- Synthetic earlier memory", memory.Text),
            first => Assert.Equal(new AssistantInput(AssistantInputKind.User, "First"), first),
            voice => Assert.Equal(new AssistantInput(AssistantInputKind.Assistant, AssistantPrompt.VoiceMarker + "Said on a call"), voice),
            second => Assert.Equal(new AssistantInput(AssistantInputKind.User, "Second"), second));
        Assert.Contains(SyntheticFamily.BandCode, step.Input[0].Text, StringComparison.Ordinal);
        var audit = Assert.Single(_audits.Added);
        Assert.Equal(
            (GenerateAssistantReply.AuditAction, "household", family.Household.Id.Value.ToString(), AuditOutcome.Succeeded),
            (audit.Action, audit.ResourceType, audit.ResourceId, audit.Outcome));
    }

    [Fact]
    public async Task Screens_the_tools_offer_come_back_as_actions()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(
            ScriptedAssistantModel.Calls((SuggestAppScreenTool.Name, """{"screen":"services"}""")),
            ScriptedAssistantModel.Reply("Open All services."));
        var tools = new RecordingToolRunner(_ => new AssistantToolResult("""{"shown":true}""", "ok", [AssistantScreens.Services], []));
        var (useCase, _) = Create(model, family, tools: tools);

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, "Where is help?")), TestContext.Current.CancellationToken);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal([AssistantScreens.Services], outcome.Result.Value.Actions);
    }

    [Fact]
    public async Task A_crisis_message_is_recorded_and_the_reply_carries_the_notice_with_sos_first()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(
            ScriptedAssistantModel.Calls((SuggestAppScreenTool.Name, """{"screen":"services"}""")),
            ScriptedAssistantModel.Reply("I am here for you."));
        var tools = new RecordingToolRunner(_ => new AssistantToolResult(
            """{"shown":true}""", "ok", [AssistantScreens.Services, AssistantScreens.Home, AssistantScreens.Help], []));
        var (useCase, _) = Create(model, family, tools: tools);

        var outcome = await useCase.HandleAsync(
            Request((AssistantMessageRole.User, "Hello"), (AssistantMessageRole.Assistant, "Hi"), (AssistantMessageRole.User, CrisisMessage)),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal((SafetyNotices.CrisisSupport, SafetyNotices.CrisisSupport), (outcome.SafetyNotice, outcome.Result.Value.SafetyNotice));
        Assert.Equal([AssistantScreens.Sos, AssistantScreens.Services, AssistantScreens.Home], outcome.Result.Value.Actions);
        Assert.Equal(
            new SafetySignal(SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, "suicide.intent", AssistantLanguages.English),
            Assert.Single(_recorder.Signals));
        Assert.Equal(AuditOutcome.Succeeded, Assert.Single(_audits.Added).Outcome);
    }

    [Fact]
    public async Task Only_the_persons_latest_message_is_screened()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var (useCase, _) = Create(new ScriptedAssistantModel(ScriptedAssistantModel.Reply("Glad to hear it.")), family);

        var outcome = await useCase.HandleAsync(
            Request((AssistantMessageRole.User, CrisisMessage), (AssistantMessageRole.Assistant, "I am here."), (AssistantMessageRole.User, "I feel better now")),
            TestContext.Current.CancellationToken);

        Assert.Null(outcome.SafetyNotice);
        Assert.Empty(_recorder.Calls);
    }

    [Fact]
    public async Task Without_a_model_a_crisis_message_is_still_recorded_and_the_failure_carries_the_notice()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("unused")) { IsConfigured = false };
        var (useCase, _) = Create(model, family);

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), TestContext.Current.CancellationToken);

        Assert.Equal((AssistantErrors.Unavailable, SafetyNotices.CrisisSupport), (outcome.Result.Error, outcome.SafetyNotice));
        Assert.Equal(SafetyDetector.PhraseScreen, Assert.Single(_recorder.Signals).Detector);
        Assert.Empty(model.Steps);
        Assert.Empty(_audits.Added);
    }

    [Fact]
    public async Task A_failed_model_after_a_crisis_message_still_carries_the_notice()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var (useCase, _) = Create(new ScriptedAssistantModel(ScriptedAssistantModel.Fail()), family);

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), TestContext.Current.CancellationToken);

        Assert.Equal((AssistantErrors.ModelFailed, SafetyNotices.CrisisSupport), (outcome.Result.Error, outcome.SafetyNotice));
        Assert.Single(_recorder.Calls);
        Assert.Equal(AuditOutcome.Failed, Assert.Single(_audits.Added).Outcome);
    }

    [Fact]
    public async Task A_recording_that_fails_still_gives_the_notice()
    {
        _recorder = new RecordingSafetySignalRecorder { Outcome = SafetyAlertOutcome.Failed };
        var family = SyntheticFamily.HeadSignsIn();
        var (useCase, _) = Create(new ScriptedAssistantModel(ScriptedAssistantModel.Reply("I am here for you.")), family);

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), TestContext.Current.CancellationToken);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal(SafetyNotices.CrisisSupport, outcome.Result.Value.SafetyNotice);
        Assert.Equal([AssistantScreens.Sos], outcome.Result.Value.Actions);
    }

    [Fact]
    public async Task A_cancelled_request_still_records_the_crisis_message_without_the_requests_token()
    {
        // The person sends the message and closes the app: the request's token has ended, the alert must still be raised.
        // The recorder bounds the recording itself (RecordSafetySignal.RecordingLimit).
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Result.Success(AssistantModelTurn.Reply("never"));
        });
        var (useCase, _) = Create(model, family, AssistantLimits.Default);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await request.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), request.Token));

        var call = Assert.Single(_recorder.Calls);
        Assert.False(call.TokenWasCancelled);
        Assert.NotEqual(request.Token, call.Token);
    }

    [Fact]
    public async Task A_caller_lookup_that_hangs_past_the_recording_budget_still_answers_with_the_notice()
    {
        // The recorder looks the caller up in a scope of its own, so its time-out cannot spoil the request's own lookup.
        var clock = new ManualTimeProvider(Now);
        var hanging = new HangingCallerAccess();
        await using var services = new ServiceCollection().AddMetrics().AddScoped<ICallerAccess>(_ => hanging).BuildServiceProvider();
        var logs = new CapturedLogs<RecordSafetySignal>();
        var recorder = new RecordSafetySignal(
            services.GetRequiredService<IServiceScopeFactory>(),
            new SafeguardingMetrics(services.GetRequiredService<IMeterFactory>()),
            clock,
            logs);
        var family = SyntheticFamily.HeadSignsIn();
        var (useCase, queries) = Create(
            new ScriptedAssistantModel(ScriptedAssistantModel.Reply("I am here for you.")), family, recorder: recorder);

        var replying = useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), TestContext.Current.CancellationToken);
        await hanging.Entered.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(RecordSafetySignal.RecordingLimit);
        var outcome = await replying;

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal(SafetyNotices.CrisisSupport, outcome.Result.Value.SafetyNotice);
        Assert.Equal([AssistantScreens.Sos], outcome.Result.Value.Actions);
        Assert.Equal(["Safety alert recording failed (safety_alert_failed, TaskCanceledException)"], logs.Lines);
        Assert.Equal(1, queries.Lookups);
        var audit = Assert.Single(_audits.Added);
        Assert.Equal((family.Household.Id.Value.ToString(), AuditOutcome.Succeeded), (audit.ResourceId, audit.Outcome));
    }

    [Theory]
    [InlineData(AssistantSafetyVerdict.DangerToSelf, SafetyConcern.SuicideOrSelfHarm)]
    [InlineData(AssistantSafetyVerdict.DangerOfViolence, SafetyConcern.AbuseOrDanger)]
    public async Task Moderation_danger_without_a_phrase_match_is_recorded_as_ai_moderation(AssistantSafetyVerdict verdict, SafetyConcern concern)
    {
        var family = SyntheticFamily.HeadSignsIn();
        var (useCase, _) = Create(
            new ScriptedAssistantModel(ScriptedAssistantModel.Reply("I am here for you.")), family, safety: new FixedSafetyCheck(verdict));

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, "zqx words the list does not hold")), TestContext.Current.CancellationToken);

        Assert.Equal(
            new SafetySignal(SafetyAlertSource.AssistantChat, concern, SafetyDetector.AiModeration, null, AssistantLanguages.English),
            Assert.Single(_recorder.Signals));
        Assert.Equal(SafetyNotices.CrisisSupport, outcome.Result.Value.SafetyNotice);
        Assert.Equal([AssistantScreens.Sos], outcome.Result.Value.Actions);
    }

    [Fact]
    public async Task Moderation_danger_about_the_concern_the_phrase_matched_records_nothing_more()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var (useCase, _) = Create(
            new ScriptedAssistantModel(ScriptedAssistantModel.Reply("I am here for you.")),
            family,
            safety: new FixedSafetyCheck(AssistantSafetyVerdict.DangerToSelf));

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, CrisisMessage)), TestContext.Current.CancellationToken);

        Assert.Equal(SafetyDetector.PhraseScreen, Assert.Single(_recorder.Signals).Detector);
        Assert.Equal([AssistantScreens.Sos], outcome.Result.Value.Actions);
    }

    [Fact]
    public async Task Moderation_danger_about_another_concern_than_the_phrase_is_recorded_too()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var (useCase, _) = Create(
            new ScriptedAssistantModel(ScriptedAssistantModel.Reply("I am here for you.")),
            family,
            safety: new FixedSafetyCheck(AssistantSafetyVerdict.DangerToSelf));

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, "I am going to kill him")), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new SafetySignal(SafetyAlertSource.AssistantChat, SafetyConcern.HarmToOthers, SafetyDetector.PhraseScreen, "harm_others.intent", AssistantLanguages.English),
                new SafetySignal(SafetyAlertSource.AssistantChat, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.AiModeration, null, AssistantLanguages.English),
            ],
            _recorder.Signals);
        Assert.Equal(SafetyNotices.CrisisSupport, outcome.Result.Value.SafetyNotice);
        Assert.Equal([AssistantScreens.Sos], outcome.Result.Value.Actions);
    }

    public static TheoryData<string> Failures => new() { "failure", "exception", "empty" };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_failed_call_is_model_failed_and_audited_as_failed(string failure)
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(failure switch
        {
            "failure" => ScriptedAssistantModel.Fail(),
            "exception" => ScriptedAssistantModel.Throw(),
            _ => ScriptedAssistantModel.Reply("  ** ** "),
        });
        var (useCase, _) = Create(model, family);

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, "Hello")), TestContext.Current.CancellationToken);

        Assert.Equal((AssistantErrors.ModelFailed, (string?)null), (outcome.Result.Error, outcome.SafetyNotice));
        Assert.Equal(AuditOutcome.Failed, Assert.Single(_audits.Added).Outcome);
    }

    [Fact]
    public async Task A_model_that_hangs_is_cut_off_at_the_time_limit()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.HangIgnoringCancellation());
        var (useCase, _) = Create(model, family);
        var clock = Stopwatch.StartNew();

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, "Hello")), TestContext.Current.CancellationToken);

        clock.Stop();
        Assert.Equal(AssistantErrors.ModelFailed, outcome.Result.Error);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"Took {clock.Elapsed}.");
        Assert.Equal(AuditOutcome.Failed, Assert.Single(_audits.Added).Outcome);
    }

    [Fact]
    public async Task The_callers_own_cancellation_propagates()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Result.Success(AssistantModelTurn.Reply("never"));
        });
        var (useCase, _) = Create(model, family, AssistantLimits.Default);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        caller.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.HandleAsync(Request((AssistantMessageRole.User, "Hello")), caller.Token));
        Assert.Empty(_audits.Added);
    }

    [Fact]
    public async Task A_caller_without_a_household_is_answered_with_a_null_household_context()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("A synthetic reply."));
        var access = new CallerAccess(new FakeCurrentUser(true, SyntheticFamily.Subject), new FixedCallerQueries(null));
        var useCase = UseCase(model, family.BuilderFor(null), access);

        var outcome = await useCase.HandleAsync(Request((AssistantMessageRole.User, "Hello")), TestContext.Current.CancellationToken);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal($"{AssistantPrompt.ContextHeading}\n{{\"household\":null}}", Assert.Single(model.Steps).Input[0].Text);
        var audit = Assert.Single(_audits.Added);
        Assert.Equal(("household", (string?)null, AuditOutcome.Succeeded), (audit.ResourceType, audit.ResourceId, audit.Outcome));
    }

    [Fact]
    public void Requests_and_replies_never_print_their_text()
    {
        var reply = new AssistantReplyResponse("secret words", Now, [], [], "secret memory", SafetyNotices.CrisisSupport);
        var printed = string.Join(
            ' ',
            new AssistantReplyRequest(AssistantLanguages.English, [new AssistantMessageRequest(AssistantMessageRole.User, "secret words")], "secret memory"),
            new AssistantMessageRequest(AssistantMessageRole.User, "secret words"),
            new AssistantMessage(AssistantMessageRole.User, "secret words"),
            new AssistantInput(AssistantInputKind.User, "secret words"),
            new AssistantModelStep("secret", [new AssistantInput(AssistantInputKind.User, "secret")], [], [new AssistantToolOutputItem("c", "secret")], true),
            new AssistantModelTurn("secret", [new AssistantToolCall("c", "t", "secret")], [new AssistantModelOutputItem("secret")], AssistantModelUsage.None),
            new AssistantToolResult("secret", "ok", [], []),
            new AssistantMemoryRequest("secret", "secret", [new AssistantMessage(AssistantMessageRole.User, "secret")]),
            new AssistantAgentRequest(null, AssistantLanguages.English, "secret", [new AssistantMessage(AssistantMessageRole.User, "secret")]),
            new AssistantAgentResult("secret", [], [], "secret", 1, AssistantSafetyVerdict.DangerToSelf),
            new ConsultantStartMessage("start", "secret", [new AssistantMessageRequest(AssistantMessageRole.User, "secret")]),
            reply,
            new AssistantReplyOutcome(Result.Success(reply), SafetyNotices.CrisisSupport));

        Assert.DoesNotContain("secret", printed, StringComparison.Ordinal);
    }
}
