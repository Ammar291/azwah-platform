using System.Net;
using System.Net.WebSockets;
using System.Text;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>
/// The live consultant over a test server's WebSocket. The shared host has no model, as production does; tests that
/// need one run a host of their own with <see cref="EchoVoiceModel"/>, which repeats back what it hears. All audio here
/// is synthetic bytes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ConsultantEndpointTests(ApiFactory factory)
{
    private static byte[] Audio(int bytes, byte fill = 1) => Enumerable.Repeat(fill, bytes).ToArray();

    private const string CrisisLine = "I want to end my life";

    private const string ArabicCrisisLine = "ابي اموت";

    private static ConsultantTranscript Said(ConsultantSpeaker speaker, string text, bool final = true) => new(speaker, text, final);

    private Task<List<AuditEvent>> StartsAsync(string subject, CancellationToken cancellationToken) =>
        AuditsAsync(subject, StartConsultantSession.AuditAction, cancellationToken);

    private Task<List<AuditEvent>> AuditsAsync(string subject, string action, CancellationToken cancellationToken) =>
        factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == subject && audit.Action == action)
            .ToListAsync(cancellationToken));

    [Fact]
    public async Task Without_a_model_the_socket_says_unavailable_and_closes_normally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        using var socket = await factory.ConnectLiveAsync(subject, cancellationToken);

        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal(2, frames.Count);
        Assert.Equal("""{"type":"unavailable"}""", Encoding.UTF8.GetString(frames[0].Data));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, frames[1].CloseStatus);
        Assert.Empty(await StartsAsync(subject, cancellationToken));
    }

    [Fact]
    public async Task A_live_session_is_ready_relays_audio_both_ways_forwards_captions_and_ends_on_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(factory, cancellationToken, Rosters.Head());
        var model = new EchoVoiceModel();
        await using var host = factory.With(consultant: model);
        using var socket = await host.ConnectLiveAsync(enrolment.Subject, cancellationToken, language: AssistantLanguages.Arabic);

        var ready = await socket.ReceiveAsync(cancellationToken);
        var pcm = Audio(3_200, fill: 7);
        await socket.SendAudioAsync(pcm, cancellationToken);
        var userCaption = await socket.ReceiveAsync(cancellationToken);
        var echoed = await socket.ReceiveAsync(cancellationToken);
        var consultantCaption = await socket.ReceiveAsync(cancellationToken);
        var turnEnd = await socket.ReceiveAsync(cancellationToken);
        await socket.SendTextAsync("""{"type":"end"}""", cancellationToken);
        var closed = await socket.ReceiveAsync(cancellationToken);

        Assert.Equal("""{"type":"ready","protocol":2,"inputSampleRate":24000,"outputSampleRate":24000}""", Encoding.UTF8.GetString(ready.Data));
        Assert.Equal(
            $$"""{"type":"transcript","speaker":"user","text":"{{EchoVoiceSession.UserCaption(pcm.Length)}}","final":true}""",
            Encoding.UTF8.GetString(userCaption.Data));
        Assert.Equal(WebSocketMessageType.Binary, echoed.Type);
        Assert.Equal(pcm, echoed.Data);
        Assert.Equal(
            $$"""{"type":"transcript","speaker":"consultant","text":"{{EchoVoiceSession.ConsultantCaption}}","final":false}""",
            Encoding.UTF8.GetString(consultantCaption.Data));
        Assert.Equal("""{"type":"turn_end"}""", Encoding.UTF8.GetString(turnEnd.Data));
        Assert.Equal((WebSocketMessageType.Close, WebSocketCloseStatus.NormalClosure), (closed.Type, closed.CloseStatus));
        var request = Assert.Single(model.Requests);
        Assert.Equal(AssistantLanguages.Arabic, request.Language);
        Assert.NotNull(request.Context);
        Assert.Equal(enrolment.Household.Reference, request.Context.Household.Reference);
        var session = Assert.Single(model.Sessions);
        Assert.Equal(1, session.FramesReceived);
        await WaitUntilAsync(() => session.Disposed, cancellationToken);
        var audit = Assert.Single(await StartsAsync(enrolment.Subject, cancellationToken));
        Assert.Equal(
            ("household", enrolment.Household.Id.ToString(), AuditOutcome.Succeeded),
            (audit.ResourceType, audit.ResourceId, audit.Outcome));
    }

    [Fact]
    public async Task The_memory_and_messages_the_app_starts_with_reach_the_model_with_the_agents_tools()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = new EchoVoiceModel();
        await using var host = factory.With(consultant: model);
        using var socket = await host.ConnectLiveAsync(
            ApiClients.NewSubject("participant"),
            cancellationToken,
            start: """{"type":"start","memory":" - Synthetic memory ","messages":[{"role":"user","text":"Synthetic typed"},{"role":"assistant","text":"Synthetic spoken","channel":"voice"}]}""");

        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);
        await socket.SendTextAsync("""{"type":"end"}""", cancellationToken);
        await socket.ReceiveUntilCloseAsync(cancellationToken);

        var request = Assert.Single(model.Requests);
        Assert.Equal("- Synthetic memory", request.Memory);
        Assert.Equal(
            [
                new AssistantMessage(AssistantMessageRole.User, "Synthetic typed"),
                new AssistantMessage(AssistantMessageRole.Assistant, "Synthetic spoken", AssistantMessageChannel.Voice),
            ],
            request.History);
        Assert.Equal([SearchSupportDirectoryTool.Name, SuggestAppScreenTool.Name], request.Tools.Definitions.Select(tool => tool.Name));
    }

    public static TheoryData<string> BadStarts => new()
    {
        "zqx not json",
        """{"type":"hello"}""",
        """{"type":"start","messages":[{"role":"user","text":"   "}]}""",
        """{"type":"start","messages":[{"role":"robot","text":"Hi"}]}""",
    };

    [Theory]
    [MemberData(nameof(BadStarts))]
    public async Task A_first_message_that_is_not_a_valid_start_ends_the_call_before_any_model_is_asked(string start)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        var model = new EchoVoiceModel();
        await using var host = factory.With(consultant: model);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken, start: start);

        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal(["""{"type":"error","code":"bad_start"}"""], frames.SkipLast(1).Select(frame => Encoding.UTF8.GetString(frame.Data)));
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, frames[^1].CloseStatus);
        Assert.Empty(model.Requests);
        Assert.Empty(await StartsAsync(subject, cancellationToken));
    }

    [Fact]
    public async Task An_app_that_sends_no_start_in_time_is_told_bad_start()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = new EchoVoiceModel();
        await using var host = factory.With(consultant: model);
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken, start: null);

        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal("""{"type":"error","code":"bad_start"}""", Encoding.UTF8.GetString(frames[0].Data));
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, frames[^1].CloseStatus);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Screens_the_consultant_offers_are_forwarded_and_unknown_ones_dropped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(consultant: new EchoVoiceModel());
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);

        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);
        await socket.SendAudioAsync(Audio(640, fill: EchoVoiceModel.ActionMarker), cancellationToken);
        var action = await socket.ReceiveAsync(cancellationToken);
        var next = await socket.ReceiveAsync(cancellationToken);
        await socket.SendTextAsync("""{"type":"end"}""", cancellationToken);
        await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal("""{"type":"action","action":"sos"}""", Encoding.UTF8.GetString(action.Data));
        Assert.Equal("transcript", next.MessageType);
    }

    [Fact]
    public async Task An_interruption_is_forwarded_and_a_close_from_the_app_ends_the_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = new EchoVoiceModel();
        await using var host = factory.With(consultant: model);
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);

        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);
        await socket.SendAudioAsync(Audio(640, fill: EchoVoiceModel.InterruptMarker), cancellationToken);
        var interrupted = await socket.ReceiveAsync(cancellationToken);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken);

        Assert.Equal("""{"type":"interrupted"}""", Encoding.UTF8.GetString(interrupted.Data));
        Assert.Equal((WebSocketState.Closed, WebSocketCloseStatus.NormalClosure), (socket.State, socket.CloseStatus));
        await WaitUntilAsync(() => Assert.Single(model.Sessions).Disposed, cancellationToken);
    }

    [Fact]
    public async Task A_model_that_fails_to_open_is_reported_and_audited_as_failed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        await using var host = factory.With(consultant: new EchoVoiceModel { FailToOpen = true });
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);

        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal(["""{"type":"error","code":"model_failed"}"""], frames.SkipLast(1).Select(frame => Encoding.UTF8.GetString(frame.Data)));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, frames[^1].CloseStatus);
        Assert.Equal(AuditOutcome.Failed, Assert.Single(await StartsAsync(subject, cancellationToken)).Outcome);
    }

    [Fact]
    public async Task A_model_that_fails_while_running_ends_the_session_with_model_failed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(consultant: new EchoVoiceModel { Behaviour = EchoBehaviour.FailOnAudio });
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);

        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);
        await socket.SendAudioAsync(Audio(640), cancellationToken);
        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal("""{"type":"error","code":"model_failed"}""", Encoding.UTF8.GetString(frames[^2].Data));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, frames[^1].CloseStatus);
    }

    [Fact]
    public async Task When_the_models_own_socket_drops_the_app_is_told_model_failed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(consultant: new EchoVoiceModel { Behaviour = EchoBehaviour.SocketDropsOnAudio });
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);

        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);
        await socket.SendAudioAsync(Audio(640), cancellationToken);
        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal("""{"type":"error","code":"model_failed"}""", Encoding.UTF8.GetString(frames[^2].Data));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, frames[^1].CloseStatus);
    }

    [Fact]
    public async Task A_model_that_ends_its_session_closes_the_socket_normally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(consultant: new EchoVoiceModel { Behaviour = EchoBehaviour.EndAfterFirstFrame });
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);

        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);
        await socket.SendAudioAsync(Audio(640), cancellationToken);
        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal("turn_end", frames[^2].MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, frames[^1].CloseStatus);
    }

    [Fact]
    public async Task A_session_without_audio_from_the_app_ends_as_idle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(consultant: new EchoVoiceModel(), limits: AssistantHosts.QuickLimits(idle: TimeSpan.FromMilliseconds(300)));
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);

        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal(
            ["ready", "error"],
            frames.SkipLast(1).Select(frame => frame.MessageType));
        Assert.Equal("""{"type":"error","code":"idle"}""", Encoding.UTF8.GetString(frames[1].Data));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, frames[^1].CloseStatus);
    }

    [Fact]
    public async Task A_session_over_its_time_limit_ends_as_too_long_after_relaying_what_came_before()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(
            consultant: new EchoVoiceModel(),
            limits: AssistantHosts.QuickLimits(session: TimeSpan.FromMilliseconds(800), idle: TimeSpan.FromSeconds(30)));
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal(
            ["transcript", null, "transcript", "turn_end", "error"],
            frames.SkipLast(1).Select(frame => frame.MessageType));
        Assert.Equal("""{"type":"error","code":"too_long"}""", Encoding.UTF8.GetString(frames[^2].Data));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, frames[^1].CloseStatus);
    }

    [Fact]
    public async Task A_frame_over_64_kilobytes_closes_the_session_as_too_big()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = new EchoVoiceModel();
        await using var host = factory.With(consultant: model);
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(ConsultantProtocol.MaxFrameBytes + 1), cancellationToken);
        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, frames[^1].CloseStatus);
        Assert.Equal(0, Assert.Single(model.Sessions).FramesReceived);
    }

    [Fact]
    public async Task A_frame_of_exactly_64_kilobytes_is_relayed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = new EchoVoiceModel();
        await using var host = factory.With(consultant: model);
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(ConsultantProtocol.MaxFrameBytes), cancellationToken);
        Assert.Equal("transcript", (await socket.ReceiveAsync(cancellationToken)).MessageType);
        var echoed = await socket.ReceiveAsync(cancellationToken);
        await socket.SendTextAsync("""{"type":"end"}""", cancellationToken);
        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal(ConsultantProtocol.MaxFrameBytes, echoed.Data.Length);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, frames[^1].CloseStatus);
    }

    [Fact]
    public async Task A_second_start_by_the_same_subject_closes_the_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        var model = new EchoVoiceModel();
        await using var host = factory.With(consultant: model);
        using var first = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await first.ReceiveAsync(cancellationToken)).MessageType);

        using var second = await host.ConnectLiveAsync(subject, cancellationToken);
        var firstFrames = await first.ReceiveUntilCloseAsync(cancellationToken);
        var secondReady = await second.ReceiveAsync(cancellationToken);
        await second.SendAudioAsync(Audio(320), cancellationToken);
        var stillRelayed = await second.ReceiveAsync(cancellationToken);
        await second.SendTextAsync("""{"type":"end"}""", cancellationToken);
        await second.ReceiveUntilCloseAsync(cancellationToken);

        Assert.Equal((WebSocketCloseStatus.NormalClosure, "replaced"), (firstFrames[^1].CloseStatus, firstFrames[^1].CloseDescription));
        Assert.Equal(("ready", "transcript"), (secondReady.MessageType, stillRelayed.MessageType));
        Assert.Equal(2, (await StartsAsync(subject, cancellationToken)).Count);
    }

    [Fact]
    public async Task A_request_that_is_not_a_websocket_is_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsParticipant();

        using var plain = await client.GetAsync("/api/v1/consultant/live?language=en", cancellationToken);
        using var badLanguage = await client.GetAsync("/api/v1/consultant/live?language=fr", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
        Assert.Equal("assistant.websocket_required", await plain.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, badLanguage.StatusCode);
        Assert.Contains("language", await badLanguage.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task A_websocket_with_an_unknown_language_is_refused_before_the_upgrade()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var refused = await Assert.ThrowsAnyAsync<Exception>(() =>
            factory.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken, language: "fr"));

        Assert.Contains("400", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_callers_and_staff_are_refused_before_the_upgrade()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(consultant: new EchoVoiceModel());

        var anonymous = await Assert.ThrowsAnyAsync<Exception>(() => host.ConnectLiveAsync(null, cancellationToken));
        var researcher = await Assert.ThrowsAnyAsync<Exception>(() =>
            host.ConnectLiveAsync(ApiClients.NewSubject("researcher"), cancellationToken, Roles.Researcher));
        using var anonymousHttp = factory.As(null);
        using var unauthorized = await anonymousHttp.GetAsync("/api/v1/consultant/live?language=en", cancellationToken);

        Assert.Contains("401", anonymous.Message, StringComparison.Ordinal);
        Assert.Contains("403", researcher.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Neither_audio_nor_captions_reach_a_log()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: new EchoVoiceModel(), logs: logs);
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        await socket.ReceiveAsync(cancellationToken);
        await socket.SendTextAsync("""{"type":"end"}""", cancellationToken);
        await socket.ReceiveUntilCloseAsync(cancellationToken);

        var relay = typeof(FamilyResilience.Api.Assistant.ConsultantRelay).FullName;
        await WaitUntilAsync(() => logs.Entries.Any(entry => entry.Category == relay), cancellationToken);
        var ended = Assert.Single(logs.Entries, entry => entry.Category == relay);
        Assert.Equal((LogLevel.Information, "A live consultant session ended: ended."), (ended.Level, ended.Message));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("Synthetic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task User_final_transcript_with_crisis_text_records_one_alert_and_sends_safety_then_sos()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(factory, cancellationToken, Rosters.Head());
        var model = new EchoVoiceModel
        {
            Script = [[Said(ConsultantSpeaker.User, ArabicCrisisLine, final: false), Said(ConsultantSpeaker.User, ArabicCrisisLine)]],
        };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(enrolment.Subject, cancellationToken, language: AssistantLanguages.Arabic);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await ReceiveManyAsync(socket, 4, cancellationToken);
        var rest = await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(["transcript", "transcript", "safety", "action"], frames.Select(frame => frame.MessageType));
        Assert.Equal([false, true], frames.Take(2).Select(frame => frame.Json.GetProperty("final").GetBoolean()));
        Assert.Equal("""{"type":"safety","notice":"crisis_support"}""", Encoding.UTF8.GetString(frames[2].Data));
        Assert.Equal("""{"type":"action","action":"sos"}""", Encoding.UTF8.GetString(frames[3].Data));
        Assert.Equal(WebSocketMessageType.Close, Assert.Single(rest).Type);
        var alert = Assert.Single(await factory.AlertsOfAsync(enrolment.Subject, cancellationToken));
        Assert.Equal(
            (SafetyConcern.SuicideOrSelfHarm, SafetySeverity.Critical, SafetyAlertSource.ConsultantVoice, SafetyDetector.PhraseScreen),
            (alert.Concern, alert.Severity, alert.Source, alert.Detector));
        Assert.Equal(("suicide.wish_to_die", "ar", 1, SafetyAlertStatus.Open), (alert.SignalCode, alert.Language, alert.OccurrenceCount, alert.Status));
        Assert.Equal((enrolment.Household.Id, enrolment.Self), (alert.HouseholdId?.Value, alert.MemberId?.Value));
        var raised = Assert.Single(await factory.AlertAuditsAsync(alert.Id, cancellationToken));
        Assert.Equal(
            (RecordSafetySignal.Raised, AuditActorType.System, (string?)null, RecordSafetySignal.ResourceType),
            (raised.Action, raised.ActorType, raised.ActorId, raised.ResourceType));
        var stored = await factory.StoredRowsOfAsync(enrolment.Subject, cancellationToken);
        Assert.Contains(stored, row => row.Contains(alert.Id.Value.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(stored, row => row.Contains("اموت", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_crisis_lines_in_one_call_record_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel
        {
            Script = [[Said(ConsultantSpeaker.User, CrisisLine), Said(ConsultantSpeaker.User, "I still want to kill myself"), new ConsultantTurnEnded()]],
        };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await ReceiveManyAsync(socket, 5, cancellationToken);
        await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(["transcript", "safety", "action", "transcript", "turn_end"], frames.Select(frame => frame.MessageType));
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal((SafetyAlertSource.ConsultantVoice, 1), (alert.Source, alert.OccurrenceCount));
        Assert.Equal([RecordSafetySignal.Raised], (await factory.AlertAuditsAsync(alert.Id, cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task Consultant_speaking_crisis_words_raises_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel { Script = [[Said(ConsultantSpeaker.Consultant, CrisisLine), new ConsultantTurnEnded()]] };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await ReceiveManyAsync(socket, 2, cancellationToken);
        await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(["transcript", "turn_end"], frames.Select(frame => frame.MessageType));
        Assert.Empty(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.DoesNotContain(await factory.StoredRowsOfAsync(subject, cancellationToken), row => row.Contains("safeguarding.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Moderation_flag_event_records_ai_moderation_alert_and_sends_safety()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel
        {
            // As the live session does: the flag, then its own SOS beside it.
            Script =
            [
                [
                    Said(ConsultantSpeaker.User, "zqx words the phrase list does not hold"),
                    new ConsultantSafetyFlag(SafetyConcern.HarmToOthers, SafetyDetector.AiModeration),
                    new ConsultantAction(AssistantScreens.Sos),
                    new ConsultantTurnEnded(),
                ],
            ],
        };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await ReceiveManyAsync(socket, 4, cancellationToken);
        await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(["transcript", "safety", "action", "turn_end"], frames.Select(frame => frame.MessageType));
        Assert.Equal("""{"type":"safety","notice":"crisis_support"}""", Encoding.UTF8.GetString(frames[1].Data));
        Assert.Equal("""{"type":"action","action":"sos"}""", Encoding.UTF8.GetString(frames[2].Data));
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal(
            (SafetyConcern.HarmToOthers, SafetySeverity.Critical, SafetyAlertSource.ConsultantVoice, SafetyDetector.AiModeration, (string?)null, "en"),
            (alert.Concern, alert.Severity, alert.Source, alert.Detector, alert.SignalCode, alert.Language));
    }

    [Fact]
    public async Task A_crisis_line_after_the_call_offered_sos_sends_the_safety_message_without_a_second_sos()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel
        {
            Script = [[new ConsultantAction(AssistantScreens.Sos), Said(ConsultantSpeaker.User, CrisisLine), new ConsultantTurnEnded()]],
        };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await ReceiveManyAsync(socket, 4, cancellationToken);
        await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(["action", "transcript", "safety", "turn_end"], frames.Select(frame => frame.MessageType));
        Assert.Equal(SafetyDetector.PhraseScreen, Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken)).Detector);
    }

    [Fact]
    public async Task A_second_concern_in_the_call_is_recorded_too_but_the_safety_message_goes_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel
        {
            Script =
            [
                [
                    Said(ConsultantSpeaker.User, CrisisLine),
                    new ConsultantSafetyFlag(SafetyConcern.HarmToOthers, SafetyDetector.AiModeration),
                    new ConsultantAction(AssistantScreens.Sos),
                    new ConsultantSafetyFlag(SafetyConcern.SuicideOrSelfHarm, SafetyDetector.ProviderFilter),
                    new ConsultantTurnEnded(),
                ],
            ],
        };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await ReceiveManyAsync(socket, 5, cancellationToken);
        await EndAsync(socket, logs, cancellationToken);

        // The relay's SOS for the phrase match, then the session's own beside its flag (the app shows a screen once).
        Assert.Equal(["transcript", "safety", "action", "action", "turn_end"], frames.Select(frame => frame.MessageType));
        var alerts = await factory.AlertsOfAsync(subject, cancellationToken);
        Assert.Equal(
            [(SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, 1), (SafetyConcern.HarmToOthers, SafetyDetector.AiModeration, 1)],
            alerts.OrderBy(alert => alert.Concern).Select(alert => (alert.Concern, alert.Detector, alert.OccurrenceCount)));
    }

    [Fact]
    public async Task A_crisis_line_still_growing_when_the_person_hangs_up_is_recorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel { Script = [[Said(ConsultantSpeaker.User, CrisisLine, final: false)]] };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var growing = await socket.ReceiveAsync(cancellationToken);
        var rest = await EndAsync(socket, logs, cancellationToken);

        Assert.False(growing.Json.GetProperty("final").GetBoolean());
        Assert.Equal(WebSocketMessageType.Close, Assert.Single(rest).Type);
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal(
            (SafetyAlertSource.ConsultantVoice, SafetyDetector.PhraseScreen, "suicide.intent", "en", 1),
            (alert.Source, alert.Detector, alert.SignalCode, alert.Language, alert.OccurrenceCount));
    }

    [Fact]
    public async Task A_growing_crisis_phrase_that_settles_into_ordinary_words_raises_nothing_when_the_call_ends()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel
        {
            Script =
            [
                [
                    Said(ConsultantSpeaker.User, CrisisLine, final: false),
                    Said(ConsultantSpeaker.User, "I want to end my lifelong habit of skipping breakfast"),
                ],
            ],
        };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await ReceiveManyAsync(socket, 2, cancellationToken);
        var rest = await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(["transcript", "transcript"], frames.Select(frame => frame.MessageType));
        Assert.Equal(WebSocketMessageType.Close, Assert.Single(rest).Type);
        Assert.Empty(await factory.AlertsOfAsync(subject, cancellationToken));
    }

    [Fact]
    public async Task A_recording_that_is_held_up_never_holds_up_the_call()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recorder = new HeldSafetySignalRecorder();
        var model = new EchoVoiceModel { Script = [[Said(ConsultantSpeaker.User, CrisisLine), new ConsultantTurnEnded()]] };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs, recorder: recorder);
        using var socket = await host.ConnectLiveAsync(ApiClients.NewSubject("participant"), cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var flagged = await ReceiveManyAsync(socket, 4, cancellationToken);
        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var echoed = await ReceiveManyAsync(socket, 4, cancellationToken);
        await WaitUntilAsync(() => recorder.Started == 1, cancellationToken);
        recorder.Release();
        await EndAsync(socket, logs, cancellationToken);

        // Everything came while the only recording was still held (it is released only now) and then failed.
        Assert.Equal(["transcript", "safety", "action", "turn_end"], flagged.Select(frame => frame.MessageType));
        Assert.Equal(["transcript", null, "transcript", "turn_end"], echoed.Select(frame => frame.MessageType));
        Assert.Equal(1, recorder.Started);
    }

    /// <summary>What the person said, which the phrase list does not hold, so only the provider's filter reacts to it.</summary>
    private const string FilteredLine = "zqx words the phrase list does not hold";

    /// <summary>The first session: the person speaks, the consultant starts a reply, and OpenAI's filter stops it (D-141).</summary>
    private static IReadOnlyList<IReadOnlyList<VoiceConsultantEvent>> StoppedByTheFilter(string said = FilteredLine) =>
    [
        [
            Said(ConsultantSpeaker.User, said),
            new ConsultantAudio(new byte[] { 9, 9 }),
            Said(ConsultantSpeaker.Consultant, "zqx cut reply", final: false),
            new ConsultantContentStopped(),
        ],
    ];

    [Fact]
    public async Task When_the_filter_stops_the_call_the_app_gets_safety_and_sos_first_and_the_call_carries_on_in_a_new_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel { SessionScripts = [StoppedByTheFilter()] };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var stopped = await ReceiveManyAsync(socket, 11, cancellationToken);
        await socket.SendAudioAsync(Audio(640), cancellationToken);
        var carriedOn = await ReceiveManyAsync(socket, 4, cancellationToken);
        var rest = await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(
            ["transcript", null, "transcript", "safety", "action", "interrupted", "transcript", null, "transcript", "turn_end", "transcript"],
            stopped.Select(frame => frame.MessageType));
        Assert.Equal("""{"type":"safety","notice":"crisis_support"}""", Encoding.UTF8.GetString(stopped[3].Data));
        Assert.Equal("""{"type":"action","action":"sos"}""", Encoding.UTF8.GetString(stopped[4].Data));
        Assert.Equal("""{"type":"transcript","speaker":"consultant","text":"","final":true}""", Encoding.UTF8.GetString(stopped[6].Data));
        Assert.Equal(EchoVoiceSession.SpokenCaption, stopped[8].Json.GetProperty("text").GetString());
        Assert.Equal(["transcript", null, "transcript", "turn_end"], carriedOn.Select(frame => frame.MessageType));
        Assert.Equal(EchoVoiceSession.UserCaption(640), carriedOn[0].Json.GetProperty("text").GetString());
        Assert.Equal((WebSocketMessageType.Close, WebSocketCloseStatus.NormalClosure), (rest[^1].Type, rest[^1].CloseStatus));

        Assert.Equal(2, model.Sessions.Count);
        Assert.True(model.Sessions[0].Disposed);
        Assert.Equal(1, model.Sessions[1].FramesReceived);
        Assert.Equal([AssistantPrompt.CrisisFallbackWords(AssistantLanguages.English)], model.Sessions[1].Spoken);
        var resumed = model.Requests[1];
        Assert.Equal((ConsultantOpening.Listen, AssistantPrompt.CrisisResumeNote), (resumed.Opening, resumed.Note));
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal(
            (SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.ConsultantVoice, SafetyDetector.ProviderFilter, (string?)null, "en", 1),
            (alert.Concern, alert.Source, alert.Detector, alert.SignalCode, alert.Language, alert.OccurrenceCount));
        Assert.Contains(logs.Entries, entry => entry.Message == "A live consultant call carried on after the provider stopped its model session: person.");
        Assert.Contains(logs.Entries, entry => entry.Message == "A live consultant session ended: ended.");
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("zqx", StringComparison.Ordinal) || entry.Message.Contains("Synthetic", StringComparison.Ordinal));
        Assert.DoesNotContain(await factory.StoredRowsOfAsync(subject, cancellationToken), row => row.Contains("zqx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_stop_in_the_call_carries_on_again_without_a_second_alert_or_notice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel
        {
            SessionScripts = [StoppedByTheFilter(), [[Said(ConsultantSpeaker.User, "zqx more hard words"), new ConsultantContentStopped()]]],
        };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        await ReceiveManyAsync(socket, 11, cancellationToken);
        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var again = await ReceiveManyAsync(socket, 7, cancellationToken);
        await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(["transcript", "action", "interrupted", null, "transcript", "turn_end", "transcript"], again.Select(frame => frame.MessageType));
        Assert.Equal(3, model.Sessions.Count);
        Assert.True(model.Sessions[1].Disposed);
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal((SafetyDetector.ProviderFilter, 1), (alert.Detector, alert.OccurrenceCount));
    }

    [Fact]
    public async Task At_its_limit_the_call_hears_the_closing_words_then_ends_normally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel { SessionScripts = [StoppedByTheFilter()] };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs, limits: AssistantHosts.QuickLimits() with { ConsultantResumeLimit = 0 });
        using var socket = await host.ConnectLiveAsync(subject, cancellationToken);
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await socket.ReceiveUntilCloseAsync(cancellationToken);

        // The relay logs how the call ended only once the alert heard in it is recorded.
        await WaitUntilAsync(() => logs.Entries.Any(entry => entry.Message == "A live consultant session ended: model_ended."), cancellationToken);
        Assert.Equal(
            ["transcript", null, "transcript", "safety", "action", "interrupted", "transcript", null, "transcript", "turn_end", "transcript", null],
            frames.Select(frame => frame.MessageType));
        Assert.Equal((WebSocketCloseStatus.NormalClosure, "model_ended"), (frames[^1].CloseStatus, frames[^1].CloseDescription));
        Assert.Equal([AssistantPrompt.CrisisClosingWords(AssistantLanguages.English)], model.Sessions[1].Spoken);
        Assert.Equal(0, model.Sessions[1].FramesReceived);
        Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Contains(logs.Entries, entry => entry.Message == "A live consultant call carried on after the provider stopped its model session: limit.");
    }

    [Fact]
    public async Task A_call_whose_earlier_conversation_trips_the_filter_reopens_once_without_it_and_raises_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel { SessionOpenings = [[new ConsultantContentStopped()]] };
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(
            subject,
            cancellationToken,
            start: """{"type":"start","memory":"- Synthetic memory","messages":[{"role":"user","text":"zqx a line the phrase list does not hold","channel":"voice"}]}""");
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await WaitUntilAsync(() => model.Sessions.Count == 2, cancellationToken);
        await Task.Delay(200, cancellationToken);
        await socket.SendAudioAsync(Audio(320), cancellationToken);
        var frames = await ReceiveManyAsync(socket, 4, cancellationToken);
        await EndAsync(socket, logs, cancellationToken);

        Assert.Equal(["transcript", null, "transcript", "turn_end"], frames.Select(frame => frame.MessageType));
        Assert.Single(model.Requests[0].History);
        var reopened = model.Requests[1];
        Assert.Equal((ConsultantOpening.Greet, (string?)null, (string?)null), (reopened.Opening, reopened.Memory, reopened.Note));
        Assert.Empty(reopened.History);
        Assert.Equal(1, model.Sessions[1].FramesReceived);
        Assert.Empty(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Contains(logs.Entries, entry => entry.Message == "A live consultant call carried on after the provider stopped its model session: seeded.");
    }

    [Fact]
    public async Task Earlier_lines_with_a_crisis_phrase_never_reach_the_model_and_raise_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var model = new EchoVoiceModel();
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(consultant: model, logs: logs);
        using var socket = await host.ConnectLiveAsync(
            subject,
            cancellationToken,
            start: $$"""{"type":"start","memory":null,"messages":[{"role":"user","text":"Synthetic typed"},{"role":"user","text":"{{CrisisLine}}","channel":"voice"}]}""");
        Assert.Equal("ready", (await socket.ReceiveAsync(cancellationToken)).MessageType);

        await EndAsync(socket, logs, cancellationToken);

        var request = Assert.Single(model.Requests);
        Assert.Equal([new AssistantMessage(AssistantMessageRole.User, "Synthetic typed")], request.History);
        Assert.Equal(AssistantPrompt.WithheldHistoryNote, request.Note);
        Assert.Empty(await factory.AlertsOfAsync(subject, cancellationToken));
    }

    private static async Task<List<LiveFrame>> ReceiveManyAsync(WebSocket socket, int count, CancellationToken cancellationToken)
    {
        var frames = new List<LiveFrame>();
        while (frames.Count < count)
        {
            frames.Add(await socket.ReceiveAsync(cancellationToken));
        }

        return frames;
    }

    /// <summary>
    /// Ends the call as the app does and waits until the relay has finished with it: it logs how the call ended only after
    /// the alerts heard in the call are recorded. Returns what came after the end, the close frame included.
    /// </summary>
    private static async Task<List<LiveFrame>> EndAsync(WebSocket socket, CapturingLoggerProvider logs, CancellationToken cancellationToken)
    {
        await socket.SendTextAsync("""{"type":"end"}""", cancellationToken);
        var rest = await socket.ReceiveUntilCloseAsync(cancellationToken);
        var relay = typeof(FamilyResilience.Api.Assistant.ConsultantRelay).FullName;
        await WaitUntilAsync(() => logs.Entries.Any(entry => entry.Category == relay && entry.Message.StartsWith("A live consultant session ended", StringComparison.Ordinal)), cancellationToken);
        return rest;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(50, cancellationToken);
        }

        Assert.True(condition());
    }
}
