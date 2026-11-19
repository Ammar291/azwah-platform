using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Domain.Common;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// <see cref="IAssistantModel"/> over OpenAI's Responses API (D-119, D-122). Each agent step is one stateless request
/// (<c>store: false</c>): the instructions, the data notes as developer messages, the conversation, the function tools
/// (strict schemas) and whatever earlier steps produced, handed back verbatim with their encrypted reasoning. A dropped
/// connection, 429 or 5xx is tried once more after a short pause (<see cref="OpenAiResponses"/>). It logs one fixed code
/// when a call goes wrong and never the context, the conversation, the reply or anything OpenAI sends back.
/// </summary>
public sealed partial class OpenAiAssistantModel(OpenAiSettings settings, HttpClient http, ILogger<OpenAiAssistantModel> logger)
    : IAssistantModel
{
    /// <summary>Room for a reply of about 200 words in Arabic, which takes more tokens, plus the model's reasoning.</summary>
    private const int MaxOutputTokens = 2_000;

    /// <summary>A memory of 150 words in Arabic, plus reasoning.</summary>
    private const int MemoryOutputTokens = 1_200;

    /// <summary>The pause before the one retry; tests set it to zero.</summary>
    public TimeSpan RetryPause { get; init; } = TimeSpan.FromMilliseconds(400 + Random.Shared.Next(400));

    public bool IsConfigured => settings.HasKey;

    public async Task<Result<AssistantModelTurn>> NextAsync(AssistantModelStep step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        var body = new JsonObject
        {
            ["model"] = settings.ChatModel,
            ["instructions"] = step.Instructions,
            ["input"] = Input(step),
            ["store"] = false,
            ["include"] = new JsonArray("reasoning.encrypted_content"),
            ["reasoning"] = new JsonObject { ["effort"] = settings.ChatReasoningEffort },
            ["max_output_tokens"] = MaxOutputTokens,
        };
        if (step.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(step.Tools.Select(Tool).ToArray<JsonNode?>());
            body["tool_choice"] = step.ToolsAllowed ? "auto" : "none";
            body["parallel_tool_calls"] = true;
        }

        var root = await PostAsync(body, cancellationToken);
        return root is null ? Result.Failure<AssistantModelTurn>(AssistantErrors.ModelFailed) : ReadTurn(root.RootElement, root);
    }

    public async Task<Result<string>> SummarizeAsync(AssistantMemoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new JsonObject
        {
            ["model"] = settings.ChatModel,
            ["instructions"] = request.Instructions,
            ["input"] = new JsonArray(
                Message("developer", request.PreviousMemory is { } memory
                    ? $"Previous memory (data):\n{memory}"
                    : "There is no previous memory."),
                Message("user", $"Conversation to fold into the memory (data):\n{AssistantPrompt.MemoryTranscript(request.Messages)}")),
            ["store"] = false,
            ["reasoning"] = new JsonObject { ["effort"] = settings.ChatReasoningEffort },
            ["max_output_tokens"] = MemoryOutputTokens,
        };
        using var root = await PostAsync(body, cancellationToken);
        if (root is null)
        {
            return Result.Failure<string>(AssistantErrors.ModelFailed);
        }

        var turn = ReadTurn(root.RootElement, document: null);
        return turn.IsSuccess && turn.Value.Text is { Length: > 0 } text
            ? Result.Success(text)
            : Result.Failure<string>(AssistantErrors.ModelFailed);
    }

    private static JsonArray Input(AssistantModelStep step)
    {
        var input = new JsonArray();
        foreach (var item in step.Input)
        {
            input.Add(Message(
                item.Kind switch
                {
                    AssistantInputKind.Data => "developer",
                    AssistantInputKind.Assistant => "assistant",
                    _ => "user",
                },
                item.Text));
        }

        foreach (var item in step.Continuation)
        {
            input.Add(item switch
            {
                AssistantModelOutputItem produced => JsonNode.Parse(produced.Json),
                AssistantToolOutputItem output => new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = output.CallId,
                    ["output"] = output.Output,
                },
                _ => throw new InvalidOperationException("Unknown continuation item."),
            });
        }

        return input;
    }

    private static JsonObject Message(string role, string content) => new() { ["role"] = role, ["content"] = content };

    internal static JsonObject Tool(AssistantToolDefinition definition) => new()
    {
        ["type"] = "function",
        ["name"] = definition.Name,
        ["description"] = definition.Description,
        ["parameters"] = JsonNode.Parse(definition.ParametersSchema),
        ["strict"] = true,
    };

    /// <summary>The parsed answer of a completed 200, or null when the call failed (after its one retry).</summary>
    private Task<JsonDocument?> PostAsync(JsonObject body, CancellationToken cancellationToken) =>
        OpenAiResponses.PostAsync(
            http, settings, body.ToJsonString(), new OpenAiRetry(AfterBusyAnswer: true, RetryPause), code => LogFailed(logger, code), cancellationToken);

    /// <summary>
    /// The step's tool calls (with every output item to hand back) or its text; a refusal, a cut-off answer, or neither
    /// text nor calls is a failure. Disposes <paramref name="document"/> when given.
    /// </summary>
    private Result<AssistantModelTurn> ReadTurn(JsonElement root, JsonDocument? document)
    {
        using var owned = document;
        if (root.ValueKind != JsonValueKind.Object || Text(root, "status") is not { } status)
        {
            return Failed("not_completed");
        }

        if (status != "completed")
        {
            return Failed(status == "incomplete" ? "incomplete" : "not_completed");
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return Failed("no_text");
        }

        var text = new StringBuilder();
        var calls = new List<AssistantToolCall>();
        var produced = new List<AssistantModelOutputItem>();
        foreach (var item in output.EnumerateArray())
        {
            produced.Add(new AssistantModelOutputItem(item.GetRawText()));
            switch (Text(item, "type"))
            {
                case "function_call":
                    if (Text(item, "call_id") is { } callId && Text(item, "name") is { } name)
                    {
                        calls.Add(new AssistantToolCall(callId, name, Text(item, "arguments") ?? "{}"));
                    }

                    break;
                case "message" when item.TryGetProperty("content", out var contents) && contents.ValueKind == JsonValueKind.Array:
                    foreach (var content in contents.EnumerateArray())
                    {
                        switch (Text(content, "type"))
                        {
                            case "refusal":
                                return Failed("refused");
                            case "output_text" when Text(content, "text") is { } part:
                                text.Append(part);
                                break;
                        }
                    }

                    break;
            }
        }

        var usage = Usage(root);
        if (calls.Count > 0)
        {
            return Result.Success(new AssistantModelTurn(null, calls, produced, usage));
        }

        var reply = text.ToString().Trim();
        return reply.Length == 0 ? Failed("no_text") : Result.Success(AssistantModelTurn.Reply(reply, usage));
    }

    private static AssistantModelUsage Usage(JsonElement root) =>
        root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            ? new AssistantModelUsage(Number(usage, "input_tokens"), Number(usage, "output_tokens"))
            : AssistantModelUsage.None;

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private Result<AssistantModelTurn> Failed(string code)
    {
        LogFailed(logger, code);
        return Result.Failure<AssistantModelTurn>(AssistantErrors.ModelFailed);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The assistant model call failed: {Code}.")]
    private static partial void LogFailed(ILogger logger, string code);
}
