using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyResilience.Application.Assistant;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// <see cref="IVoiceConsultantModel"/> over OpenAI's GPT-Live, full duplex (D-124). The server holds the WebSocket to
/// <c>wss://api.openai.com/v1/live/sessions</c>, so neither the key nor the family context reaches the phone. It starts
/// the session (the short voice instructions with the memory, PCM 24 kHz both ways, the earlier conversation as
/// <c>input</c> with Azwah's note after it as a developer message when there is one, and Responses delegation to the
/// agent's backend instructions, context and tools), waits for <c>session.started</c>, asks for a one-sentence greeting
/// unless the session is to listen (one that carries a call on, D-141), and hands back an <see cref="OpenAiLiveSession"/>.
/// It logs one fixed code when a start goes wrong and never the context or anything OpenAI sends back.
/// </summary>
public sealed partial class OpenAiLiveConsultantModel(
    OpenAiSettings settings,
    IRealtimeConnector connector,
    IAssistantSafetyCheck safety,
    AssistantMetrics metrics,
    ILogger<OpenAiLiveConsultantModel> logger) : IVoiceConsultantModel
{
    /// <summary>The PCM rate GPT-Live takes and gives here; the protocol's rate too (D-124).</summary>
    internal const int SampleRate = 24_000;

    /// <summary>At most this many of the latest messages seed a call; the memory and the backend hold the rest.</summary>
    internal const int HistoryMessages = 20;

    private const string LiveAddress = "wss://api.openai.com/v1/live/sessions";

    private static readonly JsonSerializerOptions EventOptions = new(JsonSerializerDefaults.General);

    public bool IsConfigured => settings.HasKey;

    public Task<IVoiceConsultantSession> OpenAsync(VoiceConsultantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OpenAsync(Task.FromResult(request), cancellationToken);
    }

    /// <summary>
    /// Connects first, while the request is still being prepared (D-141: the earlier conversation's safety check takes 0.7
    /// to 2.6 s, the connect 1.5 to 2.8 s), then starts the session once it is ready.
    /// </summary>
    public async Task<IVoiceConsultantSession> OpenAsync(Task<VoiceConsultantRequest> preparing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparing);
        var connection = await connector.ConnectAsync(new Uri(LiveAddress), settings.ApiKey ?? string.Empty, cancellationToken);
        try
        {
            var request = await preparing.WaitAsync(cancellationToken);
            await connection.SendAsync(Encode(SessionStart(request)), cancellationToken);
            await WaitUntilStartedAsync(connection, cancellationToken);
            if (request.Opening == ConsultantOpening.Greet)
            {
                await connection.SendAsync(
                    Encode(new JsonObject
                    {
                        ["type"] = "session.commentary.append",
                        ["event_id"] = "azwah_greeting",
                        ["delegation_id"] = null,
                        ["content"] = AssistantPrompt.Greeting(request.Language, continuing: request.History.Count > 0 || request.Note is not null),
                    }),
                    cancellationToken);
            }

            // A session that listens says nothing until spoken to (measured 2026-11-10: 15 s of silence, no words).
            return new OpenAiLiveSession(connection, request.Tools, request.Language, safety, metrics, logger);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal JsonObject SessionStart(VoiceConsultantRequest request)
    {
        var memory = AssistantMemory.Normalise(request.Memory);
        var instructions = AssistantPrompt.VoiceInstructions(request.Language);
        if (memory is not null)
        {
            instructions += $"\n\n{AssistantPrompt.MemoryHeading}\n{memory}";
        }

        var input = new JsonArray();
        foreach (var message in request.History.TakeLast(HistoryMessages))
        {
            var user = message.Role == AssistantMessageRole.User;
            input.Add(new JsonObject
            {
                ["type"] = "message",
                ["role"] = user ? "user" : "assistant",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = user ? "input_text" : "output_text",
                    ["text"] = message.Channel == AssistantMessageChannel.Voice ? message.Text : $"(typed in the chat) {message.Text}",
                }),
            });
        }

        if (request.Note is { } note)
        {
            // Azwah's own words, after the history (D-141): a developer message, as GPT-Live's guide seeds trusted context.
            input.Add(new JsonObject
            {
                ["type"] = "message",
                ["role"] = "developer",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = note }),
            });
        }

        return new JsonObject
        {
            ["type"] = "session.start",
            ["event_id"] = "azwah_start",
            ["session"] = new JsonObject
            {
                ["model"] = settings.VoiceModel,
                ["instructions"] = instructions,
                ["audio"] = new JsonObject
                {
                    ["format"] = new JsonObject { ["type"] = "audio/pcm", ["rate"] = SampleRate },
                    ["output"] = new JsonObject { ["voice"] = settings.Voice },
                },
                ["input"] = input,
                ["delegation"] = new JsonObject
                {
                    ["type"] = "responses",
                    ["responses"] = new JsonObject
                    {
                        ["model"] = settings.DelegateModel,
                        ["instructions"] = AssistantPrompt.DelegateInstructions(request.Language, request.Context, memory),
                        ["tools"] = new JsonArray(request.Tools.Definitions.Select(OpenAiAssistantModel.Tool).ToArray<JsonNode?>()),
                        ["tool_choice"] = "auto",
                        ["parallel_tool_calls"] = false,
                        ["reasoning"] = new JsonObject { ["effort"] = settings.DelegateReasoningEffort },
                    },
                },
            },
        };
    }

    internal static byte[] Encode(JsonNode value) => JsonSerializer.SerializeToUtf8Bytes(value, EventOptions);

    /// <summary>Reads until OpenAI confirms the session; an error, a close or a closed session first means the start failed.</summary>
    private async Task WaitUntilStartedAsync(IRealtimeConnection connection, CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await connection.ReceiveAsync(cancellationToken);
            if (message is null)
            {
                LogStartFailed(logger, "closed");
                throw new InvalidOperationException("The voice model closed before the session started.");
            }

            switch (OpenAiLiveSession.TypeOf(message))
            {
                case "session.started":
                    return;
                case "error":
                    LogStartFailed(logger, "refused");
                    throw new InvalidOperationException("The voice model refused the session.");
                case "session.closed":
                    LogStartFailed(logger, "session_closed");
                    throw new InvalidOperationException("The voice model closed the session before it started.");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The voice consultant model did not start: {Code}.")]
    private static partial void LogStartFailed(ILogger logger, string code);
}
