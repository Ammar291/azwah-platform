using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Domain.Safeguarding;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// One live consultation on GPT-Live (D-124). The person's audio goes up as it comes; a pump reads OpenAI's events and
/// turns them into <see cref="VoiceConsultantEvent"/>s: speech audio (silent frames dropped, a <see cref="ConsultantTurnEnded"/>
/// when speech turns to silence), transcript lines for both speakers, and the screens the agent offers. When the
/// delegated backend calls a tool, the tool runs on this server for the caller (never blocking the audio) and its output
/// goes back with <c>response.item.create</c>; once that response completes, <c>response.create</c> lets it carry on.
/// Neither message takes a <c>delegation_id</c> (GPT-Live refuses one); outputs are matched by <c>call_id</c>.
/// Each final line from the person also goes to the safety check; danger emits a <see cref="ConsultantSafetyFlag"/> (for
/// the relay to record) followed by the SOS screen. When OpenAI's content filter stops the session (an <c>error</c> with
/// code <c>content_filter</c>, or <c>session.closed</c> with reason <c>content</c>), the person's open line is settled
/// (it is theirs), the consultant's cut line is dropped unsettled, nothing more is relayed or sent, and the session ends
/// with <see cref="ConsultantContentStopped"/> for <see cref="ContinuingConsultantSession"/> to carry the call on (D-141).
/// <see cref="SpeakAsync"/> has GPT-Live say Azwah's words through <c>session.instructions.append</c>. Nothing is kept or
/// logged but fixed codes.
/// </summary>
internal sealed partial class OpenAiLiveSession : IVoiceConsultantSession
{
    /// <summary>A sample this quiet or quieter counts as silence (GPT-Live's silence is exact zeros).</summary>
    internal const int SilenceLevel = 2;

    /// <summary>
    /// This many silent frames in a row (GPT-Live sends 100 ms frames, so about a second) settle the consultant's open
    /// line, so a caption stops "growing" when the consultant stops talking, not only when the person answers.
    /// </summary>
    internal const int SilentFramesToSettle = 10;

    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(3);

    /// <summary>The code of OpenAI's content filter error, as <see cref="ErrorCode"/> writes it.</summary>
    internal const string ContentFilterCode = "error_content_filter";

    /// <summary>The reason <c>session.closed</c> gives when a safety filter ended the session.</summary>
    internal const string ContentReason = "content";

    private readonly IRealtimeConnection _connection;
    private readonly IAssistantToolRunner _tools;
    private readonly string _language;
    private readonly IAssistantSafetyCheck _safety;
    private readonly AssistantMetrics _metrics;
    private readonly ILogger _logger;
    private readonly Channel<VoiceConsultantEvent> _events =
        Channel.CreateBounded<VoiceConsultantEvent>(new BoundedChannelOptions(512) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly CancellationTokenSource _lifetime = new();
    private readonly LiveTranscriptLines _lines = new();
    private readonly Dictionary<string, List<Task>> _pendingCalls = new(StringComparer.Ordinal);
    private readonly List<Task> _background = [];
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _sendingAudio = new(1, 1);
    private Task? _pump;
    private bool _speaking;
    private int _silentFrames;
    private byte? _oddByte;
    private int _eventCounter;

    /// <summary>1 once OpenAI's filter stopped the session: nothing more is relayed or sent.</summary>
    private int _stopped;

    public OpenAiLiveSession(
        IRealtimeConnection connection, IAssistantToolRunner tools, string language, IAssistantSafetyCheck safety, AssistantMetrics metrics, ILogger logger)
    {
        _connection = connection;
        _tools = tools;
        _language = language;
        _safety = safety;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
    {
        if (Stopped)
        {
            // OpenAI's filter stopped the session: nothing more goes to it.
            return;
        }

        byte[] audio;
        await _sendingAudio.WaitAsync(cancellationToken);
        try
        {
            audio = EvenBytes(pcm.Span);
        }
        finally
        {
            _sendingAudio.Release();
        }

        if (audio.Length == 0)
        {
            return;
        }

        try
        {
            await _connection.SendAsync(
                OpenAiLiveConsultantModel.Encode(new JsonObject { ["type"] = "session.input_audio.append", ["audio"] = Convert.ToBase64String(audio) }),
                cancellationToken);
        }
        catch (Exception) when (Stopped && !cancellationToken.IsCancellationRequested)
        {
            // OpenAI closes its socket right after a filter stop: a frame on its way then is dropped, not a failure.
        }
    }

    public async Task SpeakAsync(string words, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(words);

        // The words as an instruction, so they are said as given (a commentary may paraphrase), then a commentary to start
        // speaking: an instruction alone was sometimes held until the person spoke (both measured 2026-11-10).
        await _connection.SendAsync(
            OpenAiLiveConsultantModel.Encode(new JsonObject
            {
                ["type"] = "session.instructions.append",
                ["event_id"] = NextEventId("speak"),
                ["delegation_id"] = null,
                ["content"] = AssistantPrompt.SpeakWords(words),
            }),
            cancellationToken);
        await _connection.SendAsync(
            OpenAiLiveConsultantModel.Encode(new JsonObject
            {
                ["type"] = "session.commentary.append",
                ["event_id"] = NextEventId("speak_now"),
                ["delegation_id"] = null,
                ["content"] = AssistantPrompt.SpeakNow,
            }),
            cancellationToken);
    }

    private bool Stopped => Volatile.Read(ref _stopped) == 1;

    public async IAsyncEnumerable<VoiceConsultantEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _pump ??= Task.Run(() => PumpAsync(_lifetime.Token), CancellationToken.None);
        }

        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var closing = new CancellationTokenSource(CloseWait);
            await _connection.SendAsync(
                OpenAiLiveConsultantModel.Encode(new JsonObject { ["type"] = "session.close", ["event_id"] = "azwah_close" }), closing.Token);
            if (_pump is { } pump)
            {
                await pump.WaitAsync(closing.Token);
            }
        }
        catch (Exception)
        {
            // Closing politely is best effort; the connection is dropped below either way.
        }

        await _lifetime.CancelAsync();
        Task[] background;
        lock (_gate)
        {
            background = [.. _background, .. _pump is null ? [] : new[] { _pump }];
        }

        try
        {
            await Task.WhenAll(background).WaitAsync(CloseWait);
        }
        catch (Exception)
        {
            // Tool runs and the pump end with the lifetime; what they were doing no longer matters.
        }

        await _connection.DisposeAsync();
        _lifetime.Dispose();
        _sendingAudio.Dispose();
    }

    internal static string? TypeOf(byte[] message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            return Text(document.RootElement, "type");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether every sample of the frame is at most <see cref="SilenceLevel"/> from zero.</summary>
    internal static bool IsSilent(ReadOnlySpan<byte> pcm)
    {
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            if (Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i, 2))) > SilenceLevel)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The audio with an even byte count, carrying a trailing odd byte into the next frame (GPT-Live needs whole samples).</summary>
    private byte[] EvenBytes(ReadOnlySpan<byte> pcm)
    {
        var total = pcm.Length + (_oddByte is null ? 0 : 1);
        var even = total - (total % 2);
        var audio = new byte[even];
        var written = 0;
        if (_oddByte is { } first && even > 0)
        {
            audio[0] = first;
            written = 1;
            _oddByte = null;
        }

        var take = even - written;
        pcm[..take].CopyTo(audio.AsSpan(written));
        if (take < pcm.Length)
        {
            _oddByte = pcm[^1];
        }

        return audio;
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _connection.ReceiveAsync(cancellationToken) is { } message)
            {
                if (!await HandleAsync(message, cancellationToken))
                {
                    break;
                }
            }

            if (!Stopped && _lines.Close() is { } last)
            {
                await WriteAsync(last, cancellationToken);
                Check(last);
            }

            _events.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _events.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            LogFault(_logger, "stream_failed");
            _events.Writer.TryComplete(exception);
        }
    }

    /// <summary>Handles one event from OpenAI; false once the session has closed.</summary>
    private async Task<bool> HandleAsync(byte[] message, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            LogFault(_logger, "unreadable_event");
            return true;
        }

        using var owned = document;
        var root = document.RootElement;
        var type = Text(root, "type");
        if (Stopped && type is not ("error" or "session.closed"))
        {
            // After a filter stop nothing more is relayed: the rest of the cut reply, its captions and any tool call.
            return true;
        }

        switch (type)
        {
            case "session.output_audio.delta" when Text(root, "delta") is { Length: > 0 } delta:
                await AudioAsync(Convert.FromBase64String(delta), cancellationToken);
                return true;
            case "session.output_transcript.delta":
                await TranscriptAsync(ConsultantSpeaker.Consultant, root, cancellationToken);
                return true;
            case "session.input_transcript.delta":
                await TranscriptAsync(ConsultantSpeaker.User, root, cancellationToken);
                return true;
            case "response.event":
                Delegated(root);
                return true;
            case "error":
                var code = ErrorCode(root);
                LogFault(_logger, code);
                if (code == ContentFilterCode)
                {
                    // OpenAI's filter stops the session when someone speaks of self-harm (measured 2026-11-09 and
                    // 2026-11-10: the error, then session.closed with reason content, within 25 ms).
                    await StopForContentAsync(cancellationToken);
                }

                return true;
            case "session.closed":
                if (Text(root, "reason") == ContentReason)
                {
                    await StopForContentAsync(cancellationToken);
                }

                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// The filter stopped the session: the person's open line is settled (they said it; the relay screens it), the
    /// consultant's cut line is dropped without settling, and <see cref="ConsultantContentStopped"/> ends the stream.
    /// </summary>
    private async Task StopForContentAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        if (_lines.CloseFor(ConsultantSpeaker.User) is { } said)
        {
            await WriteAsync(said, cancellationToken);
        }

        _lines.Close();
        await WriteAsync(new ConsultantContentStopped(), cancellationToken);
    }

    private async Task AudioAsync(byte[] pcm, CancellationToken cancellationToken)
    {
        if (IsSilent(pcm))
        {
            if (_speaking)
            {
                _speaking = false;
                await WriteAsync(new ConsultantTurnEnded(), cancellationToken);
            }

            if (++_silentFrames == SilentFramesToSettle && _lines.CloseFor(ConsultantSpeaker.Consultant) is { } settled)
            {
                await WriteAsync(settled, cancellationToken);
            }

            return;
        }

        _speaking = true;
        _silentFrames = 0;
        await WriteAsync(new ConsultantAudio(pcm), cancellationToken);
    }

    private async Task TranscriptAsync(ConsultantSpeaker speaker, JsonElement root, CancellationToken cancellationToken)
    {
        if (Text(root, "delta") is not { Length: > 0 } delta)
        {
            return;
        }

        foreach (var line in _lines.Add(speaker, delta, Number(root, "start_ms"), Number(root, "end_ms")))
        {
            await WriteAsync(line, cancellationToken);
            Check(line);
        }
    }

    /// <summary>A delegated backend event: a function call to run, or the end of a response whose calls now have outputs.</summary>
    private void Delegated(JsonElement root)
    {
        if (Text(root, "delegation_id") is not { } delegationId
            || !root.TryGetProperty("event", out var inner) || inner.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        switch (Text(inner, "type"))
        {
            case "response.output_item.done"
                when inner.TryGetProperty("item", out var item) && Text(item, "type") == "function_call"
                    && Text(item, "call_id") is { } callId && Text(item, "name") is { } name:
                var call = new AssistantToolCall(callId, name, Text(item, "arguments") ?? "{}");
                lock (_gate)
                {
                    if (!_pendingCalls.TryGetValue(delegationId, out var calls))
                    {
                        _pendingCalls[delegationId] = calls = [];
                    }

                    var running = RunToolAsync(delegationId, call, _lifetime.Token);
                    calls.Add(running);
                    _background.Add(running);
                }

                break;
            case "response.completed" or "response.done":
                List<Task>? pending;
                lock (_gate)
                {
                    _pendingCalls.Remove(delegationId, out pending);
                }

                if (pending is { Count: > 0 })
                {
                    Track(ContinueAsync(delegationId, pending, _lifetime.Token));
                }

                break;
        }
    }

    /// <summary>Runs one delegated call and sends its output; never throws (a failure is logged as a fixed code).</summary>
    private async Task RunToolAsync(string delegationId, AssistantToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            // Off the pump, so the audio keeps flowing while the tool runs.
            await Task.Yield();
            var result = await _tools.RunAsync(call, _language, cancellationToken);
            if (Stopped)
            {
                // The filter stopped the session while the tool ran: GPT-Live refuses events now ("the session is
                // closing", measured 2026-11-10), and the call carries on in another session.
                return;
            }

            foreach (var action in result.Actions)
            {
                await WriteAsync(new ConsultantAction(action), cancellationToken);
            }

            await _connection.SendAsync(
                OpenAiLiveConsultantModel.Encode(new JsonObject
                {
                    // No delegation_id: GPT-Live refuses it here (unknown_parameter, measured 2026-11-09).
                    ["type"] = "response.item.create",
                    ["event_id"] = NextEventId("output"),
                    ["item"] = new JsonObject { ["type"] = "function_call_output", ["call_id"] = call.CallId, ["output"] = result.Output },
                }),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFault(_logger, "tool_output_failed");
        }
        catch (OperationCanceledException)
        {
            // The session is closing.
        }
    }

    /// <summary>Lets the backend carry on once every call of its response has an output; never throws.</summary>
    private async Task ContinueAsync(string delegationId, List<Task> calls, CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(calls);
            if (Stopped)
            {
                return;
            }

            await _connection.SendAsync(
                OpenAiLiveConsultantModel.Encode(new JsonObject
                {
                    ["type"] = "response.create",
                    ["event_id"] = NextEventId("continue"),
                }),
                cancellationToken);
            _metrics.ModelCalled("delegate", succeeded: true);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFault(_logger, "delegation_continue_failed");
            _metrics.ModelCalled("delegate", succeeded: false);
        }
        catch (OperationCanceledException)
        {
            // The session is closing.
        }
    }

    /// <summary>
    /// A final line from the person goes to the safety check, off the pump; danger raises a moderation flag for the relay
    /// to record, then offers the SOS screen.
    /// </summary>
    private void Check(ConsultantTranscript line)
    {
        if (line is { Final: true, Speaker: ConsultantSpeaker.User } && _safety.IsConfigured)
        {
            Track(CheckAsync(line.Text, _lifetime.Token));
        }
    }

    private async Task CheckAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var verdict = await _safety.CheckAsync(text, cancellationToken);
            if (verdict.IsDanger())
            {
                _metrics.SafetyFlagged("voice");
                await WriteAsync(new ConsultantSafetyFlag(verdict.Concern(), SafetyDetector.AiModeration), cancellationToken);
                await WriteAsync(new ConsultantAction(AssistantScreens.Sos), cancellationToken);
            }
        }
        catch (Exception)
        {
            // The check never decides the call; the voice's own SOS rule still applies.
        }
    }

    private void Track(Task task)
    {
        lock (_gate)
        {
            _background.RemoveAll(done => done.IsCompleted);
            _background.Add(task);
        }
    }

    private ValueTask WriteAsync(VoiceConsultantEvent item, CancellationToken cancellationToken) =>
        _events.Writer.TryWrite(item) ? ValueTask.CompletedTask : WriteSlowlyAsync(item, cancellationToken);

    private async ValueTask WriteSlowlyAsync(VoiceConsultantEvent item, CancellationToken cancellationToken)
    {
        try
        {
            await _events.Writer.WriteAsync(item, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            // The session is over; the event has nowhere to go.
        }
    }

    private string NextEventId(string kind) => $"azwah_{kind}_{Interlocked.Increment(ref _eventCounter)}";

    /// <summary>
    /// <c>error_</c> and OpenAI's code when it is a fixed code; else <c>error_type_</c> and the error's type when that is
    /// (an error such as a server fault may carry no code); else <c>error_event</c>. Never the message.
    /// </summary>
    private static string ErrorCode(JsonElement root)
    {
        var nested = root.TryGetProperty("error", out var inner) && inner.ValueKind == JsonValueKind.Object;
        var error = nested ? inner : root;
        if (Text(error, "code") is { } code && FixedCode().IsMatch(code))
        {
            return $"error_{code}";
        }

        return nested && Text(error, "type") is { } type && FixedCode().IsMatch(type) ? $"error_type_{type}" : "error_event";
    }

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex("^[a-z_]{1,40}$")]
    private static partial Regex FixedCode();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The voice consultant model reported a problem: {Code}.")]
    private static partial void LogFault(ILogger logger, string code);
}
