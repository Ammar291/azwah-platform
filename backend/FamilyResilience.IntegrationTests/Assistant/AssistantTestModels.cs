using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>
/// A configured text model for test hosts: answers each agent step from a function and records every step. Nothing
/// leaves the test.
/// </summary>
internal sealed class RecordingAssistantModel(Func<AssistantModelStep, Result<AssistantModelTurn>> answer) : IAssistantModel
{
    private readonly ConcurrentQueue<AssistantModelStep> _steps = new();
    private readonly ConcurrentQueue<AssistantMemoryRequest> _memoryRequests = new();

    public bool IsConfigured => true;

    public IReadOnlyList<AssistantModelStep> Steps => [.. _steps];

    public IReadOnlyList<AssistantMemoryRequest> MemoryRequests => [.. _memoryRequests];

    public static RecordingAssistantModel Replying(string reply) => new(_ => Result.Success(AssistantModelTurn.Reply(reply)));

    public static RecordingAssistantModel Failing() => new(_ => Result.Failure<AssistantModelTurn>(AssistantErrors.ModelFailed));

    /// <summary>Calls the tools on the first step, then replies.</summary>
    public static RecordingAssistantModel Calling(string reply, params (string Name, string Arguments)[] calls) => new(step =>
        Result.Success(step.Continuation.Count == 0
            ? new AssistantModelTurn(
                null,
                calls.Select((call, index) => new AssistantToolCall($"call_{index}", call.Name, call.Arguments)).ToList(),
                [new AssistantModelOutputItem("""{"type":"function_call"}""")],
                AssistantModelUsage.None)
            : AssistantModelTurn.Reply(reply)));

    /// <summary>The family context a step carried, as the JSON the model reads.</summary>
    public static JsonElement Context(AssistantModelStep step)
    {
        var text = step.Input[0].Text;
        Assert.StartsWith(AssistantPrompt.ContextHeading, text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text[(AssistantPrompt.ContextHeading.Length + 1)..]);
        return document.RootElement.Clone();
    }

    /// <summary>The family context a step carried, read back into the record (null without a household).</summary>
    public static FamilyContext? FamilyContextOf(AssistantModelStep step)
    {
        var json = Context(step);
        return json.TryGetProperty("household", out var household) && household.ValueKind == JsonValueKind.Null && !json.TryGetProperty("caller", out _)
            ? null
            : json.Deserialize<FamilyContext>(ContextOptions);
    }

    private static readonly JsonSerializerOptions ContextOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public Task<Result<AssistantModelTurn>> NextAsync(AssistantModelStep step, CancellationToken cancellationToken)
    {
        _steps.Enqueue(step);
        return Task.FromResult(answer(step));
    }

    public Task<Result<string>> SummarizeAsync(AssistantMemoryRequest request, CancellationToken cancellationToken)
    {
        _memoryRequests.Enqueue(request);
        return Task.FromResult(Result.Success("- Synthetic refreshed memory"));
    }
}

/// <summary>A configured safety check with a fixed verdict, for test hosts; it records how many texts it was given.</summary>
internal sealed class FixedAssistantSafetyCheck(AssistantSafetyVerdict verdict) : IAssistantSafetyCheck
{
    private int _checked;

    public bool IsConfigured => true;

    public int Checked => Volatile.Read(ref _checked);

    public Task<AssistantSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _checked);
        return Task.FromResult(verdict);
    }
}

/// <summary>
/// A signal recorder that holds every recording until <see cref="Release"/> and then reports it failed, for proving that
/// a call never waits for one.
/// </summary>
internal sealed class HeldSafetySignalRecorder : ISafetySignalRecorder
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;

    public int Started => Volatile.Read(ref _started);

    public void Release() => _released.TrySetResult();

    public async Task<SafetyAlertOutcome> RecordForCallerAsync(SafetySignal signal, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _started);
        await _released.Task.WaitAsync(cancellationToken);
        return SafetyAlertOutcome.Failed;
    }

    public Task<SafetyAlertOutcome> RecordForSessionAsync(
        SafetyAlertSubject subject, AssessmentSessionId sessionId, SafetySignal signal, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A call records for its caller only.");
}

/// <summary>How a test voice session behaves after the first audio frame.</summary>
internal enum EchoBehaviour
{
    /// <summary>Echo every frame back with synthetic captions and a turn end.</summary>
    Echo,

    /// <summary>Fail the event stream on the first frame.</summary>
    FailOnAudio,

    /// <summary>Echo the first frame, then end the event stream.</summary>
    EndAfterFirstFrame,

    /// <summary>The model's own socket drops when audio is sent.</summary>
    SocketDropsOnAudio,
}

/// <summary>
/// A configured voice model for test hosts that repeats back what it hears, with synthetic captions; nothing leaves the
/// test. A frame whose first byte is <see cref="InterruptMarker"/> is answered with <c>interrupted</c> first. A
/// <see cref="Script"/> answers the first frames with set events instead.
/// </summary>
internal sealed class EchoVoiceModel : IVoiceConsultantModel
{
    /// <summary>
    /// Events that answer the app's frames in turn, in place of the echo: the first frame gets the first list, the second
    /// the second, and frames after the last list are echoed.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<VoiceConsultantEvent>> Script { get; init; } = [];

    /// <summary>
    /// Scripts for the sessions of one call in turn (D-141: a call carries on in new sessions); a session past the last
    /// uses <see cref="Script"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<VoiceConsultantEvent>>> SessionScripts { get; init; } = [];

    /// <summary>Events each session produces as soon as it opens, in turn; a session past the last produces none.</summary>
    public IReadOnlyList<IReadOnlyList<VoiceConsultantEvent>> SessionOpenings { get; init; } = [];

    public const byte InterruptMarker = 0xFF;

    /// <summary>A frame starting with this byte is answered with an offered SOS screen first.</summary>
    public const byte ActionMarker = 0xFE;

    private readonly ConcurrentQueue<VoiceConsultantRequest> _requests = new();
    private readonly ConcurrentQueue<EchoVoiceSession> _sessions = new();

    public bool IsConfigured => true;

    public bool FailToOpen { get; init; }

    public EchoBehaviour Behaviour { get; init; }

    public IReadOnlyList<VoiceConsultantRequest> Requests => [.. _requests];

    public IReadOnlyList<EchoVoiceSession> Sessions => [.. _sessions];

    public Task<IVoiceConsultantSession> OpenAsync(VoiceConsultantRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        if (FailToOpen)
        {
            return Task.FromException<IVoiceConsultantSession>(new InvalidOperationException("Synthetic model fault."));
        }

        var index = _sessions.Count;
        var session = new EchoVoiceSession(
            Behaviour, index < SessionScripts.Count ? SessionScripts[index] : Script, index < SessionOpenings.Count ? SessionOpenings[index] : []);
        _sessions.Enqueue(session);
        return Task.FromResult<IVoiceConsultantSession>(session);
    }
}

internal sealed class EchoVoiceSession : IVoiceConsultantSession
{
    private readonly EchoBehaviour behaviour;
    private readonly IReadOnlyList<IReadOnlyList<VoiceConsultantEvent>> script;

    public EchoVoiceSession(
        EchoBehaviour behaviour, IReadOnlyList<IReadOnlyList<VoiceConsultantEvent>> script, IReadOnlyList<VoiceConsultantEvent>? opening = null)
    {
        this.behaviour = behaviour;
        this.script = script;
        foreach (var item in opening ?? [])
        {
            _events.Writer.TryWrite(item);
        }
    }

    public const string ConsultantCaption = "Synthetic echo";

    /// <summary>The caption a test session gives for words it was asked to say.</summary>
    public const string SpokenCaption = "Synthetic spoken words";

    private readonly Channel<VoiceConsultantEvent> _events = Channel.CreateUnbounded<VoiceConsultantEvent>();
    private readonly List<string> _spoken = [];
    private int _frames;
    private int _disposed;

    public int FramesReceived => Volatile.Read(ref _frames);

    public bool Disposed => Volatile.Read(ref _disposed) == 1;

    public static string UserCaption(int bytes) => $"Synthetic caption of {bytes} bytes";

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
    {
        var frame = Interlocked.Increment(ref _frames);
        if (frame <= script.Count)
        {
            foreach (var item in script[frame - 1])
            {
                _events.Writer.TryWrite(item);
            }

            return Task.CompletedTask;
        }

        if (behaviour == EchoBehaviour.SocketDropsOnAudio)
        {
            throw new System.Net.WebSockets.WebSocketException(System.Net.WebSockets.WebSocketError.ConnectionClosedPrematurely);
        }

        if (behaviour == EchoBehaviour.FailOnAudio)
        {
            _events.Writer.TryComplete(new InvalidOperationException("Synthetic model fault."));
            return Task.CompletedTask;
        }

        if (pcm.Span[0] == EchoVoiceModel.InterruptMarker)
        {
            _events.Writer.TryWrite(new ConsultantInterrupted());
        }

        if (pcm.Span[0] == EchoVoiceModel.ActionMarker)
        {
            _events.Writer.TryWrite(new ConsultantAction(AssistantScreens.Sos));
            _events.Writer.TryWrite(new ConsultantAction("not_a_screen"));
        }

        _events.Writer.TryWrite(new ConsultantTranscript(ConsultantSpeaker.User, UserCaption(pcm.Length), true));
        _events.Writer.TryWrite(new ConsultantAudio(pcm.ToArray()));
        _events.Writer.TryWrite(new ConsultantTranscript(ConsultantSpeaker.Consultant, ConsultantCaption, false));
        _events.Writer.TryWrite(new ConsultantTurnEnded());
        if (behaviour == EchoBehaviour.EndAfterFirstFrame)
        {
            _events.Writer.TryComplete();
        }

        return Task.CompletedTask;
    }

    public IAsyncEnumerable<VoiceConsultantEvent> ReadEventsAsync(CancellationToken cancellationToken) =>
        _events.Reader.ReadAllAsync(cancellationToken);

    public IReadOnlyList<string> Spoken
    {
        get
        {
            lock (_spoken)
            {
                return [.. _spoken];
            }
        }
    }

    /// <summary>Records the words and answers with them as the consultant's audio, caption and turn end, as GPT-Live would say them.</summary>
    public Task SpeakAsync(string words, CancellationToken cancellationToken)
    {
        lock (_spoken)
        {
            _spoken.Add(words);
        }

        _events.Writer.TryWrite(new ConsultantAudio(new byte[] { 0x5A, 0x5A }));
        _events.Writer.TryWrite(new ConsultantTranscript(ConsultantSpeaker.Consultant, SpokenCaption, false));
        _events.Writer.TryWrite(new ConsultantTurnEnded());
        _events.Writer.TryWrite(new ConsultantTranscript(ConsultantSpeaker.Consultant, SpokenCaption, true));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
