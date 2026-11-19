using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;

namespace FamilyResilience.Api.Assistant;

/// <summary>The text messages of the live consultant's protocol, version 2 (D-124), as UTF-8 JSON.</summary>
internal static class ConsultantMessages
{
    private static readonly JsonSerializerOptions StartOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public static ReadOnlyMemory<byte> Ready { get; } = Write(writer =>
    {
        writer.WriteString("type", "ready");
        writer.WriteNumber("protocol", ConsultantProtocol.Version);
        writer.WriteNumber("inputSampleRate", ConsultantProtocol.InputSampleRate);
        writer.WriteNumber("outputSampleRate", ConsultantProtocol.OutputSampleRate);
    });

    public static ReadOnlyMemory<byte> Unavailable { get; } = Write(writer => writer.WriteString("type", "unavailable"));

    public static ReadOnlyMemory<byte> TurnEnd { get; } = Write(writer => writer.WriteString("type", "turn_end"));

    public static ReadOnlyMemory<byte> Interrupted { get; } = Write(writer => writer.WriteString("type", "interrupted"));

    /// <summary>
    /// <c>{"type":"safety","notice":"crisis_support"}</c>: a crisis signal was heard in the call (at most once a call,
    /// D-138); the app shows its crisis support card and the call goes on.
    /// </summary>
    public static ReadOnlyMemory<byte> Safety { get; } = Write(writer =>
    {
        writer.WriteString("type", "safety");
        writer.WriteString("notice", SafetyNotices.CrisisSupport);
    });

    public static ReadOnlyMemory<byte> Transcript(ConsultantTranscript transcript) => Write(writer =>
    {
        writer.WriteString("type", "transcript");
        writer.WriteString("speaker", transcript.Speaker == ConsultantSpeaker.User ? "user" : "consultant");
        writer.WriteString("text", transcript.Text);
        writer.WriteBoolean("final", transcript.Final);
    });

    /// <summary><c>{"type":"action","action":…}</c> with an <see cref="AssistantScreens"/> code.</summary>
    public static ReadOnlyMemory<byte> Action(ConsultantAction action) => Write(writer =>
    {
        writer.WriteString("type", "action");
        writer.WriteString("action", action.Action);
    });

    /// <summary>
    /// <c>{"type":"error","code":…}</c> with a fixed code: <c>model_failed</c>, <c>too_long</c>, <c>idle</c> or
    /// <c>bad_start</c>.
    /// </summary>
    public static ReadOnlyMemory<byte> Error(string code) => Write(writer =>
    {
        writer.WriteString("type", "error");
        writer.WriteString("code", code);
    });

    /// <summary>Whether a text message from the app is <c>{"type":"end"}</c>; anything else is ignored.</summary>
    public static bool IsEnd(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.ValueEquals("end");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The app's <c>start</c> message as sent (validated by the caller), or null when it is not JSON of that shape.</summary>
    public static ConsultantStartMessage? ReadStart(ReadOnlyMemory<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize<ConsultantStartMessage>(json.Span, StartOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReadOnlyMemory<byte> Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return buffer.WrittenMemory;
    }
}
