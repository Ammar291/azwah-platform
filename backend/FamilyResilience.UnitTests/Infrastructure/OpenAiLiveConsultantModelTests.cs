using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Infrastructure.Assistant;
using FamilyResilience.UnitTests.Application.Assistant;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>
/// The GPT-Live adapter and its live session (D-124) on a fake socket: events are the shapes measured against OpenAI on
/// 2026-11-09, audio is synthetic bytes and every text is synthetic.
/// </summary>
public sealed class OpenAiLiveConsultantModelTests
{
    private const string Key = "sk-synthetic-live-key";

    private static string Audio(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), samples[i]);
        }

        return $$"""{"type":"session.output_audio.delta","delta":"{{Convert.ToBase64String(bytes)}}"}""";
    }

    private static string Said(string kind, string delta, long startMs) =>
        $$"""{"type":"session.{{kind}}_transcript.delta","start_ms":{{startMs}},"end_ms":{{startMs + 200}},"delta":"{{delta}}"}""";

    private static VoiceConsultantRequest Request(IAssistantToolRunner? tools = null, string? memory = null, params AssistantMessage[] history) =>
        new(null, AssistantLanguages.English, memory, history, tools ?? new RecordingToolRunner());

    private static (OpenAiLiveConsultantModel Model, FakeRealtimeConnection Connection, FakeRealtimeConnector Connector, CapturedLogs<OpenAiLiveConsultantModel> Logs) Create(
        IAssistantSafetyCheck? safety = null)
    {
        var connection = new FakeRealtimeConnection();
        var connector = new FakeRealtimeConnector(connection);
        var logs = new CapturedLogs<OpenAiLiveConsultantModel>();
        var settings = OpenAiSettings.From(key => key == OpenAiSettings.ApiKeySetting ? Key : null);
        return (new OpenAiLiveConsultantModel(settings, connector, safety ?? new NoAssistantSafetyCheck(), TestAssistantMetrics.Create(), logs), connection, connector, logs);
    }

    /// <summary>Opens a session: answers the start with session.started.</summary>
    private static async Task<IVoiceConsultantSession> OpenAsync(
        OpenAiLiveConsultantModel model, FakeRealtimeConnection connection, VoiceConsultantRequest request, CancellationToken cancellationToken)
    {
        connection.OnSend = json =>
        {
            if (json.Contains("\"type\":\"session.start\"", StringComparison.Ordinal))
            {
                connection.Push("""{"type":"session.started","session":{"id":"live_synthetic","instructions":"zqx"}}""");
            }
            else if (json.Contains("\"type\":\"session.close\"", StringComparison.Ordinal))
            {
                connection.Push("""{"type":"session.closed","usage":{"seconds":1}}""");
            }
        };
        return await model.OpenAsync(request, cancellationToken);
    }

    /// <summary>
    /// Up to <paramref name="count"/> events, fewer when the stream ends first. An event that never comes fails the test
    /// after ten seconds instead of holding the run.
    /// </summary>
    private static async Task<List<VoiceConsultantEvent>> TakeAsync(IAsyncEnumerator<VoiceConsultantEvent> events, int count)
    {
        var taken = new List<VoiceConsultantEvent>();
        while (taken.Count < count && await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
        {
            taken.Add(events.Current);
        }

        return taken;
    }

    [Fact]
    public async Task The_session_starts_on_gpt_live_with_short_voice_instructions_history_and_delegation_to_the_agent()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, connector, _) = Create();
        var history = new[]
        {
            new AssistantMessage(AssistantMessageRole.User, "zqx typed question"),
            new AssistantMessage(AssistantMessageRole.Assistant, "zqx spoken answer", AssistantMessageChannel.Voice),
        };

        await using var session = await OpenAsync(model, connection, Request(memory: " zqx memory ", history: history), token);

        var (address, key) = Assert.Single(connector.Connections);
        Assert.Equal(("wss://api.openai.com/v1/live/sessions", Key), (address.ToString(), key));
        using var start = JsonDocument.Parse(connection.Sent[0]);
        var settings = start.RootElement.GetProperty("session");
        Assert.Equal("session.start", start.RootElement.GetProperty("type").GetString());
        Assert.Equal(OpenAiSettings.DefaultVoiceModel, settings.GetProperty("model").GetString());
        Assert.StartsWith(AssistantPrompt.VoiceInstructions(AssistantLanguages.English), settings.GetProperty("instructions").GetString(), StringComparison.Ordinal);
        Assert.EndsWith($"{AssistantPrompt.MemoryHeading}\nzqx memory", settings.GetProperty("instructions").GetString(), StringComparison.Ordinal);
        Assert.Equal(("audio/pcm", 24_000), (settings.GetProperty("audio").GetProperty("format").GetProperty("type").GetString(), settings.GetProperty("audio").GetProperty("format").GetProperty("rate").GetInt32()));
        Assert.Equal(OpenAiSettings.DefaultVoice, settings.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString());
        var input = settings.GetProperty("input").EnumerateArray().ToList();
        Assert.Equal(
            [("user", "input_text", "(typed in the chat) zqx typed question"), ("assistant", "output_text", "zqx spoken answer")],
            input.Select(item => (item.GetProperty("role").GetString(), item.GetProperty("content")[0].GetProperty("type").GetString(), item.GetProperty("content")[0].GetProperty("text").GetString())));
        var delegation = settings.GetProperty("delegation");
        Assert.Equal("responses", delegation.GetProperty("type").GetString());
        var backend = delegation.GetProperty("responses");
        Assert.Equal(OpenAiSettings.DefaultChatModel, backend.GetProperty("model").GetString());
        Assert.Equal(AssistantPrompt.DelegateInstructions(AssistantLanguages.English, null, "zqx memory"), backend.GetProperty("instructions").GetString());
        Assert.Equal([SearchSupportDirectoryTool.Name, SuggestAppScreenTool.Name], backend.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()));
        Assert.False(backend.GetProperty("parallel_tool_calls").GetBoolean());
        using var greeting = JsonDocument.Parse(connection.Sent[1]);
        Assert.Equal("session.commentary.append", greeting.RootElement.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, greeting.RootElement.GetProperty("delegation_id").ValueKind);
        Assert.Equal(AssistantPrompt.Greeting(AssistantLanguages.English, continuing: true), greeting.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task The_socket_connects_while_the_request_is_still_being_prepared_and_the_session_starts_once_it_is_ready()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, connector, _) = Create();
        connection.OnSend = json =>
        {
            if (json.Contains("\"type\":\"session.start\"", StringComparison.Ordinal))
            {
                connection.Push("""{"type":"session.started","session":{"id":"live_synthetic"}}""");
            }
        };
        var preparing = new TaskCompletionSource<VoiceConsultantRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var opening = model.OpenAsync(preparing.Task, token);
        for (var attempt = 0; attempt < 100 && connector.Connections.Count == 0; attempt++)
        {
            await Task.Delay(10, token);
        }

        Assert.Single(connector.Connections);
        Assert.Empty(connection.Sent);
        preparing.SetResult(Request(memory: "zqx prepared memory"));
        await using var session = await opening.WaitAsync(TimeSpan.FromSeconds(10), token);

        using var start = JsonDocument.Parse(connection.Sent[0]);
        Assert.EndsWith("zqx prepared memory", start.RootElement.GetProperty("session").GetProperty("instructions").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_cannot_be_prepared_closes_the_early_socket()
    {
        var (model, connection, _, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            model.OpenAsync(Task.FromException<VoiceConsultantRequest>(new InvalidOperationException("Synthetic fault.")), TestContext.Current.CancellationToken));

        Assert.True(connection.Disposed);
        Assert.Empty(connection.Sent);
    }

    [Fact]
    public async Task An_error_before_the_session_starts_fails_the_open_and_closes_the_socket()
    {
        var (model, connection, _, logs) = Create();
        connection.Push("""{"type":"error","error":{"code":"invalid_request","message":"zqx provider text"}}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.OpenAsync(Request(), TestContext.Current.CancellationToken));

        Assert.True(connection.Disposed);
        Assert.DoesNotContain("zqx", Assert.Single(logs.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Speech_is_relayed_silence_is_dropped_and_the_turn_ends_when_speech_turns_to_silence()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(Audio(0, 0, 1, -2));
        connection.Push(Audio(400, -900));
        connection.Push(Audio(0, 0));
        connection.Push(Audio(0, 0));
        connection.Push(Audio(1200));
        var taken = await TakeAsync(events, 3);

        Assert.Collection(
            taken,
            first => Assert.Equal(4, Assert.IsType<ConsultantAudio>(first).Pcm.Length),
            end => Assert.IsType<ConsultantTurnEnded>(end),
            next => Assert.Equal(2, Assert.IsType<ConsultantAudio>(next).Pcm.Length));
    }

    [Fact]
    public async Task Transcript_deltas_become_growing_lines_that_settle_when_the_other_speaker_starts_or_the_call_ends()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(Said("input", " How", 1_000));
        connection.Push(Said("input", " old", 1_200));
        connection.Push(Said("output", " Fifteen.", 2_000));
        connection.Close();
        var lines = (await TakeAsync(events, 10)).Cast<ConsultantTranscript>().ToList();

        Assert.Equal(
            [
                (ConsultantSpeaker.User, "How", false),
                (ConsultantSpeaker.User, "How old", false),
                (ConsultantSpeaker.User, "How old", true),
                (ConsultantSpeaker.Consultant, "Fifteen.", false),
                (ConsultantSpeaker.Consultant, "Fifteen.", true),
            ],
            lines.Select(line => (line.Speaker, line.Text, line.Final)));
    }

    [Fact]
    public async Task The_consultants_line_settles_after_a_second_of_silence_without_waiting_for_the_person()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(Audio(800));
        connection.Push(Said("output", " All done.", 100));
        for (var frame = 0; frame < 10; frame++)
        {
            connection.Push(Audio(0, 0));
        }

        var taken = await TakeAsync(events, 4);

        Assert.IsType<ConsultantAudio>(taken[0]);
        Assert.Equal(("All done.", false), (Assert.IsType<ConsultantTranscript>(taken[1]).Text, ((ConsultantTranscript)taken[1]).Final));
        Assert.IsType<ConsultantTurnEnded>(taken[2]);
        Assert.Equal(("All done.", true), (Assert.IsType<ConsultantTranscript>(taken[3]).Text, ((ConsultantTranscript)taken[3]).Final));
    }

    [Fact]
    public async Task A_delegated_tool_call_runs_on_the_server_its_output_goes_back_and_the_backend_carries_on()
    {
        var token = TestContext.Current.CancellationToken;
        var tools = new RecordingToolRunner(_ => new AssistantToolResult("""{"shown":true}""", "ok", [AssistantScreens.Services], []));
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request(tools), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push("""{"type":"response.event","delegation_id":"item_d1","event":{"type":"response.output_item.done","item":{"type":"function_call","call_id":"call_1","name":"suggest_app_screen","arguments":"{\"screen\":\"services\"}"}}}""");
        var action = Assert.IsType<ConsultantAction>(Assert.Single(await TakeAsync(events, 1)));
        var output = await connection.NextSentAsync("response.item.create", token);
        connection.Push("""{"type":"response.event","delegation_id":"item_d1","event":{"type":"response.completed","response":{"output":[]}}}""");
        var carryOn = await connection.NextSentAsync("response.create", token);

        Assert.Equal(AssistantScreens.Services, action.Action);
        var call = Assert.Single(tools.Calls);
        Assert.Equal(("call_1", "suggest_app_screen", """{"screen":"services"}"""), (call.CallId, call.Name, call.Arguments));
        using var sent = JsonDocument.Parse(output);
        Assert.False(sent.RootElement.TryGetProperty("delegation_id", out _));
        var item = sent.RootElement.GetProperty("item");
        Assert.Equal(("function_call_output", "call_1", """{"shown":true}"""), (item.GetProperty("type").GetString(), item.GetProperty("call_id").GetString(), item.GetProperty("output").GetString()));
        using var create = JsonDocument.Parse(carryOn);
        Assert.Equal(["type", "event_id"], create.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData(AssistantSafetyVerdict.DangerToSelf, SafetyConcern.SuicideOrSelfHarm)]
    [InlineData(AssistantSafetyVerdict.DangerOfViolence, SafetyConcern.AbuseOrDanger)]
    public async Task A_final_line_from_the_person_that_the_safety_check_flags_raises_a_moderation_flag_then_offers_sos(
        AssistantSafetyVerdict verdict, SafetyConcern concern)
    {
        var token = TestContext.Current.CancellationToken;
        var safety = new FixedSafetyCheck(verdict);
        var (model, connection, _, _) = Create(safety);
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(Said("input", " zqx frightening words", 500));
        connection.Push(Said("output", " I hear you.", 1_500));
        var taken = await TakeAsync(events, 5);

        var flag = Assert.Single(taken.OfType<ConsultantSafetyFlag>());
        Assert.Equal(new ConsultantSafetyFlag(concern, SafetyDetector.AiModeration), flag);
        var sos = taken.FindIndex(item => item is ConsultantAction { Action: AssistantScreens.Sos });
        Assert.Equal(taken.IndexOf(flag) + 1, sos);
        Assert.Equal(["zqx frightening words"], safety.Checked);
    }

    [Fact]
    public async Task A_line_the_safety_check_clears_raises_no_flag()
    {
        var token = TestContext.Current.CancellationToken;
        var safety = new FixedSafetyCheck(AssistantSafetyVerdict.Clear);
        var (model, connection, _, _) = Create(safety);
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(Said("input", " zqx ordinary words", 500));
        connection.Push(Said("output", " I hear you.", 1_500));
        connection.Close();
        var taken = await TakeAsync(events, 10);

        Assert.Equal(["zqx ordinary words"], safety.Checked);
        Assert.DoesNotContain(taken, item => item is ConsultantSafetyFlag or ConsultantAction);
    }

    private const string ContentFilter =
        """{"type":"error","error":{"type":"content_filter","code":"content_filter","message":"zqx provider text","param":null}}""";

    private const string ClosedForContent = """{"type":"session.closed","reason":"content","usage":{"seconds":14.0}}""";

    [Fact]
    public async Task When_openais_filter_stops_the_session_the_persons_line_settles_and_it_ends_stopped_with_no_flag_of_its_own()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, logs) = Create();
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(Said("input", " zqx hard", 500));
        connection.Push(Said("input", " words", 700));
        connection.Push(ContentFilter);
        connection.Push(ClosedForContent);
        var taken = await TakeAsync(events, 10);

        Assert.Equal<VoiceConsultantEvent>(
            [
                new ConsultantTranscript(ConsultantSpeaker.User, "zqx hard", false),
                new ConsultantTranscript(ConsultantSpeaker.User, "zqx hard words", false),
                new ConsultantTranscript(ConsultantSpeaker.User, "zqx hard words", true),
                new ConsultantContentStopped(),
            ],
            taken);
        Assert.Contains(logs.Lines, line => line.Contains("error_content_filter", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains("zqx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_consultants_cut_reply_is_never_settled_and_nothing_after_the_filter_is_relayed()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(Said("input", " Hi", 500));
        connection.Push(Said("output", " I'm really", 1_500));
        connection.Push(Audio(500));
        connection.Push(ContentFilter);
        connection.Push(Audio(900));
        connection.Push(Said("output", " sorry", 1_800));
        connection.Push("""{"type":"response.event","delegation_id":"item_d1","event":{"type":"response.output_item.done","item":{"type":"function_call","call_id":"call_1","name":"suggest_app_screen","arguments":"{}"}}}""");
        connection.Push(ClosedForContent);
        var taken = await TakeAsync(events, 10);

        Assert.Collection(
            taken,
            item => Assert.Equal(new ConsultantTranscript(ConsultantSpeaker.User, "Hi", false), item),
            item => Assert.Equal(new ConsultantTranscript(ConsultantSpeaker.User, "Hi", true), item),
            item => Assert.Equal(new ConsultantTranscript(ConsultantSpeaker.Consultant, "I'm really", false), item),
            item => Assert.IsType<ConsultantAudio>(item),
            item => Assert.IsType<ConsultantContentStopped>(item));
    }

    [Fact]
    public async Task A_tool_that_finishes_after_the_filter_stop_sends_nothing_to_the_closing_session()
    {
        // Measured 2026-11-10: OpenAI refuses it ("The session is closing and cannot accept new client events").
        var token = TestContext.Current.CancellationToken;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new GatedToolRunner(gate.Task);
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request(tools), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push("""{"type":"response.event","delegation_id":"item_d1","event":{"type":"response.output_item.done","item":{"type":"function_call","call_id":"call_1","name":"suggest_app_screen","arguments":"{}"}}}""");
        connection.Push("""{"type":"response.event","delegation_id":"item_d1","event":{"type":"response.completed","response":{"output":[]}}}""");
        var reading = TakeAsync(events, 1);
        await tools.Started.WaitAsync(TimeSpan.FromSeconds(10), token);
        connection.Push(ContentFilter);
        Assert.IsType<ConsultantContentStopped>(Assert.Single(await reading));
        gate.SetResult();
        await Task.Delay(300, token);

        Assert.DoesNotContain(connection.Sent, json => json.Contains("\"type\":\"response.item.create\"", StringComparison.Ordinal));
        Assert.DoesNotContain(connection.Sent, json => json.Contains("\"type\":\"response.create\"", StringComparison.Ordinal));
    }

    /// <summary>Tools that wait for a gate before they answer, so a test can stop the session while one runs.</summary>
    private sealed class GatedToolRunner(Task gate) : IAssistantToolRunner
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public IReadOnlyList<AssistantToolDefinition> Definitions { get; } = [new(SuggestAppScreenTool.Name, "Synthetic screens.", """{"type":"object"}""")];

        public async Task<AssistantToolResult> RunAsync(AssistantToolCall call, string language, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await gate.WaitAsync(cancellationToken);
            return new AssistantToolResult("""{"ok":true}""", "ok", [], []);
        }
    }

    [Fact]
    public async Task A_session_closed_for_content_without_an_error_ends_stopped_too()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(ClosedForContent);
        var taken = await TakeAsync(events, 10);

        Assert.Equal<VoiceConsultantEvent>([new ConsultantContentStopped()], taken);
    }

    [Fact]
    public async Task After_the_filter_stop_audio_goes_nowhere_and_a_closed_socket_is_not_a_fault()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push(ContentFilter);
        await TakeAsync(events, 1);
        await session.SendAudioAsync(new byte[] { 1, 2 }, token);
        await session.DisposeAsync();

        Assert.DoesNotContain(connection.Sent, json => json.Contains("session.input_audio.append", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_note_follows_the_history_as_azwahs_own_developer_message_and_a_session_that_listens_does_not_greet()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        var request = Request(history: [new AssistantMessage(AssistantMessageRole.User, "zqx earlier")]) with
        {
            Opening = ConsultantOpening.Listen,
            Note = AssistantPrompt.CrisisResumeNote,
        };

        await using var session = await OpenAsync(model, connection, request, token);

        using var start = JsonDocument.Parse(connection.Sent[0]);
        var input = start.RootElement.GetProperty("session").GetProperty("input").EnumerateArray().ToList();
        Assert.Equal(
            [("user", "input_text", "(typed in the chat) zqx earlier"), ("developer", "input_text", AssistantPrompt.CrisisResumeNote)],
            input.Select(item => (item.GetProperty("role").GetString(), item.GetProperty("content")[0].GetProperty("type").GetString(), item.GetProperty("content")[0].GetProperty("text").GetString())));
        Assert.DoesNotContain(connection.Sent, json => json.Contains("session.commentary.append", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_note_without_history_greets_back()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();

        await using var session = await OpenAsync(model, connection, Request() with { Note = AssistantPrompt.WithheldHistoryNote }, token);

        using var greeting = JsonDocument.Parse(connection.Sent[1]);
        Assert.Equal(AssistantPrompt.Greeting(AssistantLanguages.English, continuing: true), greeting.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Speaking_asks_gpt_live_to_say_the_words_as_given_through_its_instructions()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request() with { Opening = ConsultantOpening.Listen }, token);

        await session.SpeakAsync("Synthetic supportive words.", token);

        using var sent = JsonDocument.Parse(await connection.NextSentAsync("session.instructions.append", token));
        Assert.Equal(JsonValueKind.Null, sent.RootElement.GetProperty("delegation_id").ValueKind);
        Assert.Equal(AssistantPrompt.SpeakWords("Synthetic supportive words."), sent.RootElement.GetProperty("content").GetString());
        Assert.StartsWith("azwah_speak_", sent.RootElement.GetProperty("event_id").GetString(), StringComparison.Ordinal);

        // An instruction alone was sometimes held until the person spoke (measured 2026-11-10); a commentary starts speech.
        using var nudge = JsonDocument.Parse(await connection.NextSentAsync("session.commentary.append", token));
        Assert.Equal(JsonValueKind.Null, nudge.RootElement.GetProperty("delegation_id").ValueKind);
        Assert.Equal(AssistantPrompt.SpeakNow, nudge.RootElement.GetProperty("content").GetString());
        var order = connection.Sent.Select(json => JsonDocument.Parse(json).RootElement.GetProperty("type").GetString()).ToList();
        Assert.True(order.IndexOf("session.instructions.append") < order.IndexOf("session.commentary.append"));
    }

    [Fact]
    public async Task An_error_without_a_code_is_logged_by_its_type_and_never_by_its_message()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, logs) = Create();
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push("""{"type":"error","error":{"type":"server_error","code":null,"message":"zqx provider text"}}""");
        connection.Push("""{"type":"error","error":{"type":"Not A Code","code":"Not-a-code","message":"zqx provider text"}}""");
        connection.Close();
        await TakeAsync(events, 10);

        Assert.Contains(logs.Lines, line => line.EndsWith("error_type_server_error.", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, line => line.EndsWith("error_event.", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains("zqx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Another_provider_error_raises_no_flag()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        await using var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        connection.Push("""{"type":"error","error":{"type":"server_error","code":"rate_limited","message":"zqx provider text"}}""");
        connection.Close();
        var taken = await TakeAsync(events, 10);

        Assert.Empty(taken);
    }

    [Fact]
    public async Task Audio_goes_up_in_whole_samples_and_disposing_closes_the_session_politely()
    {
        var token = TestContext.Current.CancellationToken;
        var (model, connection, _, _) = Create();
        var session = await OpenAsync(model, connection, Request(), token);
        await using var events = session.ReadEventsAsync(token).GetAsyncEnumerator(token);

        await session.SendAudioAsync(new byte[] { 1, 2, 3 }, token);
        await session.SendAudioAsync(new byte[] { 4 }, token);
        connection.OnSend = json =>
        {
            if (json.Contains("\"type\":\"session.close\"", StringComparison.Ordinal))
            {
                connection.Push("""{"type":"session.closed","usage":{"seconds":3}}""");
            }
        };
        await session.DisposeAsync();

        var appended = connection.Sent.Where(json => json.Contains("session.input_audio.append", StringComparison.Ordinal))
            .Select(json => Convert.FromBase64String(JsonDocument.Parse(json).RootElement.GetProperty("audio").GetString()!))
            .ToList();
        Assert.Equal([new byte[] { 1, 2 }, new byte[] { 3, 4 }], appended);
        Assert.Contains(connection.Sent, json => json.Contains("\"type\":\"session.close\"", StringComparison.Ordinal));
        Assert.True(connection.Disposed);
    }

    [Fact]
    public void Transcript_lines_close_after_a_pause_or_when_too_long()
    {
        var lines = new LiveTranscriptLines();

        lines.Add(ConsultantSpeaker.User, " One", 0, 200);
        var afterPause = lines.Add(ConsultantSpeaker.User, " Two", 200 + LiveTranscriptLines.Pause + 1, 3_000);
        var tooLong = lines.Add(ConsultantSpeaker.User, new string('x', LiveTranscriptLines.MaxLineLength), 3_000, 3_100);

        Assert.Equal([("One", true), ("Two", false)], afterPause.Select(line => (line.Text, line.Final)));
        Assert.Equal(("Two", true), (tooLong[0].Text, tooLong[0].Final));
        Assert.Null(new LiveTranscriptLines().Close());
    }
}
