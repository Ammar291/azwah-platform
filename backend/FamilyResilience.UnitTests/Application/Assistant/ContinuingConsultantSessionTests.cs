using FamilyResilience.Application.Assistant;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.UnitTests.Infrastructure;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>
/// A live call that outlives the model sessions the provider's filter stops (D-141), on scripted sessions. Every word
/// and frame here is synthetic.
/// </summary>
public sealed class ContinuingConsultantSessionTests
{
    private const string Said = "zqx synthetic words that tripped the filter";

    private static readonly AssistantLimits Limits = AssistantLimits.Default with
    {
        ModelTimeout = TimeSpan.FromSeconds(5),
        ConsultantCrisisReplyWait = TimeSpan.FromMilliseconds(300),
        ConsultantClosingWait = TimeSpan.FromSeconds(5),
    };

    private static readonly ConsultantTranscript PersonSaid = new(ConsultantSpeaker.User, Said, Final: true);

    private static readonly AssistantMessage Earlier = new(AssistantMessageRole.User, "zqx earlier line");

    private readonly CapturedLogs<ContinuingConsultantSession> _logs = new();

    private static VoiceConsultantRequest Request(string language = AssistantLanguages.English) =>
        new(null, language, "- zqx memory", [Earlier], new RecordingToolRunner());

    private async Task<ContinuingConsultantSession> OpenAsync(
        LegsVoiceModel model, IConsultantCrisisReply? crisis = null, AssistantLimits? limits = null, string language = AssistantLanguages.English)
    {
        var session = await ContinuingConsultantSession.OpenAsync(
            model, Request(language), crisis ?? new ScriptedCrisisReply(), limits ?? Limits, TestAssistantMetrics.Create(), _logs,
            TestContext.Current.CancellationToken);
        return Assert.IsType<ContinuingConsultantSession>(session);
    }

    /// <summary>Up to <paramref name="count"/> events, fewer when the stream ends first; one that never comes fails after ten seconds.</summary>
    private static async Task<List<VoiceConsultantEvent>> TakeAsync(IAsyncEnumerator<VoiceConsultantEvent> events, int count)
    {
        var taken = new List<VoiceConsultantEvent>();
        while (taken.Count < count && await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
        {
            taken.Add(events.Current);
        }

        return taken;
    }

    private static async Task<bool> EndsAsync(IAsyncEnumerator<VoiceConsultantEvent> events) =>
        !await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_first_session_opens_with_the_request_its_events_pass_through_and_the_call_ends_with_it()
    {
        var token = TestContext.Current.CancellationToken;
        var leg = new ScriptedLeg();
        var model = new LegsVoiceModel(leg);
        await using var session = await OpenAsync(model);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        await session.SendAudioAsync(new byte[] { 1, 2 }, token);
        leg.Push(new ConsultantTranscript(ConsultantSpeaker.User, "zqx hello", true), new ConsultantAudio(new byte[] { 9, 9 }), new ConsultantTurnEnded());
        leg.End();
        var taken = await TakeAsync(events, 3);

        var opened = Assert.Single(model.Requests);
        Assert.Equal((ConsultantOpening.Greet, "- zqx memory", (string?)null), (opened.Opening, opened.Memory, opened.Note));
        Assert.Equal([Earlier], opened.History);
        Assert.Equal([new byte[] { 1, 2 }], leg.Audio);
        Assert.Collection(taken, item => Assert.IsType<ConsultantTranscript>(item), item => Assert.IsType<ConsultantAudio>(item), item => Assert.IsType<ConsultantTurnEnded>(item));
        Assert.True(await EndsAsync(events));
    }

    [Fact]
    public async Task A_model_that_cannot_open_gives_no_session()
    {
        var session = await ContinuingConsultantSession.OpenAsync(
            new LegsVoiceModel(), Request(), new ScriptedCrisisReply(), Limits, TestAssistantMetrics.Create(), _logs, TestContext.Current.CancellationToken);

        Assert.Null(session);
    }

    [Fact]
    public async Task When_the_filter_stops_the_call_over_what_the_person_said_the_app_gets_the_flag_sos_and_an_interruption_and_the_cut_line_is_cleared()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg();
        await using var session = await OpenAsync(new LegsVoiceModel(first, second));
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.Push(PersonSaid, new ConsultantAudio(new byte[] { 5, 5 }), new ConsultantTranscript(ConsultantSpeaker.Consultant, "zqx cut reply", false), new ConsultantContentStopped());
        var taken = await TakeAsync(events, 7);

        Assert.Equal<VoiceConsultantEvent>(
            [
                PersonSaid,
                taken[1],
                new ConsultantTranscript(ConsultantSpeaker.Consultant, "zqx cut reply", false),
                new ConsultantSafetyFlag(SafetyConcern.SuicideOrSelfHarm, SafetyDetector.ProviderFilter),
                new ConsultantAction(AssistantScreens.Sos),
                new ConsultantInterrupted(),
                new ConsultantTranscript(ConsultantSpeaker.Consultant, string.Empty, Final: true),
            ],
            taken);
        Assert.DoesNotContain(taken, item => item is ConsultantContentStopped);
    }

    [Fact]
    public async Task The_call_carries_on_in_a_new_session_that_listens_with_the_note_and_says_the_supportive_words()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg();
        var model = new LegsVoiceModel(first, second);
        var crisis = new ScriptedCrisisReply();
        await using var session = await OpenAsync(model, crisis, language: AssistantLanguages.Arabic);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await second.SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        await session.SendAudioAsync(new byte[] { 3, 4 }, token);
        second.Push(new ConsultantTranscript(ConsultantSpeaker.User, "zqx thank you", true), new ConsultantAudio(new byte[] { 7, 7 }));
        var carriedOn = await TakeAsync(events, 2);

        Assert.True(first.Disposed);
        var resumed = model.Requests[1];
        Assert.Equal(
            (ConsultantOpening.Listen, AssistantPrompt.CrisisResumeNote, AssistantLanguages.Arabic, "- zqx memory"),
            (resumed.Opening, resumed.Note, resumed.Language, resumed.Memory));
        Assert.Equal([Earlier], resumed.History);
        Assert.Equal([(AssistantLanguages.Arabic, Said)], crisis.Calls);
        Assert.Equal([ScriptedCrisisReply.Words], second.Spoken);
        Assert.Equal([new byte[] { 3, 4 }], second.Audio);
        Assert.Collection(carriedOn, item => Assert.IsType<ConsultantTranscript>(item), item => Assert.IsType<ConsultantAudio>(item));
    }

    [Fact]
    public async Task While_no_session_listens_the_microphone_is_not_heard()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg();
        var opening = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await OpenAsync(new LegsVoiceModel(first, second), new ScriptedCrisisReply { Write = _ => opening.Task }, Limits with { ConsultantCrisisReplyWait = TimeSpan.FromSeconds(5) });
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await session.SendAudioAsync(new byte[] { 1, 1 }, token);
        opening.SetResult("zqx written words");
        await second.SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        await session.SendAudioAsync(new byte[] { 2, 2 }, token);

        Assert.Empty(first.Audio);
        Assert.Equal([new byte[] { 2, 2 }], second.Audio);
        Assert.Equal(["zqx written words"], second.Spoken);
    }

    [Theory]
    [InlineData("late")]
    [InlineData("null")]
    [InlineData("throws")]
    [InlineData("blank")]
    [InlineData("unconfigured")]
    public async Task Without_a_written_reply_in_time_the_fixed_words_are_said(string how)
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg();
        var crisis = new ScriptedCrisisReply
        {
            IsConfigured = how != "unconfigured",
            Write = how switch
            {
                "late" => cancellation => Task.Delay(Timeout.Infinite, cancellation).ContinueWith(_ => (string?)"zqx too late", TaskScheduler.Default),
                "throws" => _ => Task.FromException<string?>(new InvalidOperationException("zqx synthetic fault")),
                "blank" => _ => Task.FromResult<string?>("  **  "),
                _ => _ => Task.FromResult<string?>(null),
            },
        };
        await using var session = await OpenAsync(new LegsVoiceModel(first, second), crisis, language: AssistantLanguages.Arabic);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await second.SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);

        Assert.Equal([AssistantPrompt.CrisisFallbackWords(AssistantLanguages.Arabic)], second.Spoken);
        Assert.DoesNotContain(_logs.Lines, line => line.Contains("zqx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_new_stop_over_what_the_person_said_carries_on_again()
    {
        var token = TestContext.Current.CancellationToken;
        var legs = new[] { new ScriptedLeg(), new ScriptedLeg(), new ScriptedLeg() };
        var model = new LegsVoiceModel(legs);
        await using var session = await OpenAsync(model);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        legs[0].Push(PersonSaid, new ConsultantContentStopped());
        var firstStop = await TakeAsync(events, 4);
        await legs[1].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        legs[1].Push(new ConsultantTranscript(ConsultantSpeaker.User, "zqx again", true), new ConsultantContentStopped());
        var secondStop = await TakeAsync(events, 4);
        await legs[2].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);

        Assert.Equal(3, model.Requests.Count);
        Assert.All(model.Requests.Skip(1), request => Assert.Equal((ConsultantOpening.Listen, AssistantPrompt.CrisisResumeNote), (request.Opening, request.Note)));
        Assert.Contains(secondStop, item => item is ConsultantSafetyFlag { Detector: SafetyDetector.ProviderFilter });
        Assert.Contains(firstStop, item => item is ConsultantAction { Action: AssistantScreens.Sos });
        Assert.True(legs[0].Disposed && legs[1].Disposed);
    }

    [Fact]
    public async Task At_the_limit_the_closing_words_are_said_without_listening_and_the_call_ends_once_they_settle()
    {
        var token = TestContext.Current.CancellationToken;
        var legs = new[] { new ScriptedLeg(), new ScriptedLeg(), new ScriptedLeg() };
        var model = new LegsVoiceModel(legs);
        var crisis = new ScriptedCrisisReply();
        await using var session = await OpenAsync(model, crisis, Limits with { ConsultantResumeLimit = 1 });
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        legs[0].Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await legs[1].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        legs[1].Push(PersonSaid, new ConsultantContentStopped());
        var stop = await TakeAsync(events, 4);
        await legs[2].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        await session.SendAudioAsync(new byte[] { 4, 4 }, token);
        legs[2].Push(
            new ConsultantAudio(new byte[] { 8, 8 }),
            new ConsultantTranscript(ConsultantSpeaker.Consultant, "zqx closing", false),
            new ConsultantTurnEnded(),
            new ConsultantTranscript(ConsultantSpeaker.Consultant, "zqx closing", true));
        var closing = await TakeAsync(events, 4);

        Assert.Equal(new ConsultantSafetyFlag(SafetyConcern.SuicideOrSelfHarm, SafetyDetector.ProviderFilter), stop[1]);
        Assert.Equal([AssistantPrompt.CrisisClosingWords(AssistantLanguages.English)], legs[2].Spoken);
        Assert.Equal((ConsultantOpening.Listen, AssistantPrompt.CrisisResumeNote), (model.Requests[2].Opening, model.Requests[2].Note));
        Assert.Single(crisis.Calls);
        Assert.Empty(legs[2].Audio);
        Assert.Equal(4, closing.Count);
        Assert.True(await EndsAsync(events));
        Assert.True(legs[2].Disposed);
    }

    [Fact]
    public async Task At_the_limit_the_call_ends_after_the_closing_wait_if_the_words_never_settle()
    {
        var token = TestContext.Current.CancellationToken;
        var legs = new[] { new ScriptedLeg(), new ScriptedLeg() };
        await using var session = await OpenAsync(
            new LegsVoiceModel(legs), limits: Limits with { ConsultantResumeLimit = 0, ConsultantClosingWait = TimeSpan.FromMilliseconds(200) });
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        legs[0].Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);

        Assert.True(await EndsAsync(events));
        Assert.Equal([AssistantPrompt.CrisisClosingWords(AssistantLanguages.English)], legs[1].Spoken);
    }

    [Fact]
    public async Task A_stop_before_the_person_said_anything_reopens_once_without_the_earlier_conversation_and_raises_nothing()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg();
        var model = new LegsVoiceModel(first, second);
        var crisis = new ScriptedCrisisReply();
        await using var session = await OpenAsync(model, crisis);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.Push(new ConsultantContentStopped());
        second.Push(new ConsultantAudio(new byte[] { 6, 6 }));
        var taken = await TakeAsync(events, 1);

        Assert.IsType<ConsultantAudio>(Assert.Single(taken));
        var reopened = model.Requests[1];
        Assert.Equal((ConsultantOpening.Greet, (string?)null, (string?)null), (reopened.Opening, reopened.Memory, reopened.Note));
        Assert.Empty(reopened.History);
        Assert.Empty(crisis.Calls);
        Assert.Empty(second.Spoken);
        Assert.True(first.Disposed);
    }

    [Fact]
    public async Task A_carried_on_session_stopped_before_the_person_spoke_in_it_reopens_with_the_note_and_raises_nothing_new()
    {
        var token = TestContext.Current.CancellationToken;
        var legs = new[] { new ScriptedLeg(), new ScriptedLeg(), new ScriptedLeg() };
        var model = new LegsVoiceModel(legs);
        await using var session = await OpenAsync(model);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        legs[0].Push(new ConsultantAudio(new byte[] { 1, 1 }), PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 5);
        await legs[1].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        legs[1].Push(new ConsultantContentStopped());
        legs[2].Push(new ConsultantTurnEnded());
        var next = await TakeAsync(events, 1);

        Assert.IsType<ConsultantTurnEnded>(Assert.Single(next));
        var reopened = model.Requests[2];
        Assert.Equal((ConsultantOpening.Listen, AssistantPrompt.CrisisResumeNote, (string?)null), (reopened.Opening, reopened.Note, reopened.Memory));
        Assert.Empty(reopened.History);
        Assert.Equal([AssistantPrompt.CrisisFallbackWords(AssistantLanguages.English)], legs[2].Spoken);
    }

    [Fact]
    public async Task A_carried_on_session_stopped_after_its_words_were_heard_reopens_listening_without_saying_them_again()
    {
        var token = TestContext.Current.CancellationToken;
        var legs = new[] { new ScriptedLeg(), new ScriptedLeg(), new ScriptedLeg() };
        var model = new LegsVoiceModel(legs);
        await using var session = await OpenAsync(model);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        legs[0].Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await legs[1].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        legs[1].Push(new ConsultantAudio(new byte[] { 3, 3 }), new ConsultantContentStopped());
        await TakeAsync(events, 1);
        legs[2].Push(new ConsultantTurnEnded());
        await TakeAsync(events, 1);

        Assert.Equal(ConsultantOpening.Listen, model.Requests[2].Opening);
        Assert.Empty(legs[2].Spoken);
    }

    [Fact]
    public async Task A_second_stop_before_the_person_spoke_ends_the_call_as_a_model_failure()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg();
        await using var session = await OpenAsync(new LegsVoiceModel(first, second));
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.Push(new ConsultantContentStopped());
        second.Push(new ConsultantContentStopped());

        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await TakeAsync(events, 1));
    }

    [Fact]
    public async Task A_session_that_cannot_be_reopened_ends_the_call_as_a_model_failure_after_sos()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        await using var session = await OpenAsync(new LegsVoiceModel(first));
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.Push(PersonSaid, new ConsultantContentStopped());
        var taken = await TakeAsync(events, 4);

        Assert.Contains(taken, item => item is ConsultantAction { Action: AssistantScreens.Sos });
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await TakeAsync(events, 1));
    }

    [Fact]
    public async Task A_model_fault_still_ends_the_call_as_a_model_failure()
    {
        var token = TestContext.Current.CancellationToken;
        var leg = new ScriptedLeg();
        await using var session = await OpenAsync(new LegsVoiceModel(leg));
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        leg.Fail();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await TakeAsync(events, 1));
    }

    [Fact]
    public async Task A_carried_on_session_that_fails_before_the_person_spoke_in_it_is_reopened_once_with_the_note()
    {
        var token = TestContext.Current.CancellationToken;
        var legs = new[] { new ScriptedLeg(), new ScriptedLeg(), new ScriptedLeg() };
        var model = new LegsVoiceModel(legs);
        await using var session = await OpenAsync(model);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        legs[0].Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await legs[1].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        legs[1].Fail();
        legs[2].Push(new ConsultantTurnEnded());
        var next = await TakeAsync(events, 1);

        Assert.IsType<ConsultantTurnEnded>(Assert.Single(next));
        Assert.Equal((ConsultantOpening.Listen, AssistantPrompt.CrisisResumeNote), (model.Requests[2].Opening, model.Requests[2].Note));

        // The failed session never got its words out, so the fixed words are said: the person is not left in silence.
        Assert.Equal([AssistantPrompt.CrisisFallbackWords(AssistantLanguages.English)], legs[2].Spoken);
        Assert.Contains(_logs.Lines, line => line.EndsWith("fault.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_carried_on_session_that_fails_after_its_words_and_the_person_was_heard_reopens_listening()
    {
        // Measured 2026-11-10: a session carrying the call on dropped its socket 12 s in, after it had spoken.
        var token = TestContext.Current.CancellationToken;
        var legs = new[] { new ScriptedLeg(), new ScriptedLeg(), new ScriptedLeg() };
        var model = new LegsVoiceModel(legs);
        await using var session = await OpenAsync(model);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        legs[0].Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await legs[1].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        legs[1].Push(new ConsultantAudio(new byte[] { 3, 3 }), new ConsultantTranscript(ConsultantSpeaker.User, "zqx thank you", true));
        await TakeAsync(events, 2);
        legs[1].Fail();
        legs[2].Push(new ConsultantTurnEnded());
        var next = await TakeAsync(events, 1);

        Assert.IsType<ConsultantTurnEnded>(Assert.Single(next));
        Assert.Equal((ConsultantOpening.Listen, AssistantPrompt.CrisisResumeNote), (model.Requests[2].Opening, model.Requests[2].Note));
        Assert.Empty(legs[2].Spoken);
    }

    [Fact]
    public async Task A_second_failure_of_a_carried_on_session_ends_the_call_as_a_model_failure()
    {
        var token = TestContext.Current.CancellationToken;
        var legs = new[] { new ScriptedLeg(), new ScriptedLeg(), new ScriptedLeg(), new ScriptedLeg() };
        var model = new LegsVoiceModel(legs);
        await using var session = await OpenAsync(model);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        legs[0].Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await legs[1].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        legs[1].Fail();
        var reading = TakeAsync(events, 1);
        await legs[2].SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        legs[2].Fail();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await reading);
        Assert.Equal(3, model.Requests.Count);
    }

    [Fact]
    public async Task A_frame_lost_on_a_stopping_session_is_dropped_but_a_failing_send_on_the_listening_one_is_a_fault()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg { FailSends = new System.Net.WebSockets.WebSocketException("zqx synthetic") };
        await using var session = await OpenAsync(new LegsVoiceModel(first, second));
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.FailSends = new System.Net.WebSockets.WebSocketException("zqx synthetic");
        first.SendGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = session.SendAudioAsync(new byte[] { 1 }, token);
        first.Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        first.SendGate.SetResult();
        await sending;
        await second.SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);

        await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => session.SendAudioAsync(new byte[] { 2 }, token));
    }

    [Fact]
    public async Task A_frame_that_fails_just_before_its_session_reports_the_stop_is_dropped()
    {
        // Measured 2026-11-10: OpenAI drops its socket right after the filter's error, so a frame can fail before the
        // session has read the stop that is already on its way.
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg { FailSends = new System.Net.WebSockets.WebSocketException("zqx synthetic") };
        var second = new ScriptedLeg();
        await using var session = await OpenAsync(new LegsVoiceModel(first, second));
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        var sending = session.SendAudioAsync(new byte[] { 1 }, token);
        first.Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await sending;
        await second.SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        await session.SendAudioAsync(new byte[] { 2 }, token);

        Assert.Equal([new byte[] { 2 }], second.Audio);
    }

    [Fact]
    public async Task Disposing_closes_the_listening_session()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg();
        var session = await OpenAsync(new LegsVoiceModel(first, second));
        await using (var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token))
        {
            first.Push(PersonSaid, new ConsultantContentStopped());
            await TakeAsync(events, 4);
            await second.SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);
        }

        await session.DisposeAsync();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task Nothing_the_person_said_or_the_reply_reaches_a_log()
    {
        var token = TestContext.Current.CancellationToken;
        var first = new ScriptedLeg();
        var second = new ScriptedLeg();
        await using var session = await OpenAsync(new LegsVoiceModel(first, second), new ScriptedCrisisReply { Write = _ => Task.FromResult<string?>("zqx reply words") });
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        first.Push(PersonSaid, new ConsultantContentStopped());
        await TakeAsync(events, 4);
        await second.SpokeAsync.WaitAsync(TimeSpan.FromSeconds(10), token);

        Assert.Contains(_logs.Lines, line => line.Contains("person", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Lines, line => line.Contains("zqx", StringComparison.Ordinal));
    }
}
