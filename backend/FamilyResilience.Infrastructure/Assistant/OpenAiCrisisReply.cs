using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyResilience.Application.Assistant;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// <see cref="IConsultantCrisisReply"/> over OpenAI's Responses API (D-141): one stateless request (<c>store: false</c>)
/// with <see cref="AssistantPrompt.CrisisReplyInstructions"/> and the person's latest line as data, the crisis model
/// (<c>Assistant:Model:CrisisName</c>, <c>gpt-5.4-mini</c> by default) with no reasoning, through the shared
/// <see cref="OpenAiResponses"/> transport. A dropped connection is tried once more; a busy answer is not, because the
/// reply must come within seconds. A refusal, a cut-off answer or no text is null. It logs one fixed code when a call goes
/// wrong and never what the person said, the reply or anything OpenAI sends back.
/// </summary>
public sealed partial class OpenAiCrisisReply(OpenAiSettings settings, HttpClient http, ILogger<OpenAiCrisisReply> logger) : IConsultantCrisisReply
{
    /// <summary>Room for 70 words in Arabic, which takes more tokens.</summary>
    private const int MaxOutputTokens = 400;

    public bool IsConfigured => settings.HasKey;

    public async Task<string?> WriteAsync(string language, string said, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(said);
        var body = new JsonObject
        {
            ["model"] = settings.CrisisModel,
            ["instructions"] = AssistantPrompt.CrisisReplyInstructions(language),
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = AssistantPrompt.CrisisReplyInput(said) }),
            ["store"] = false,
            ["reasoning"] = new JsonObject { ["effort"] = settings.CrisisReasoningEffort },
            ["max_output_tokens"] = MaxOutputTokens,
        };
        using var root = await OpenAiResponses.PostAsync(
            http, settings, body.ToJsonString(), new OpenAiRetry(AfterBusyAnswer: false, TimeSpan.Zero), code => LogFailed(logger, code), cancellationToken);
        return root is null ? null : Read(root.RootElement);
    }

    private string? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || Text(root, "status") != "completed")
        {
            LogFailed(logger, "not_completed");
            return null;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            LogFailed(logger, "no_text");
            return null;
        }

        var text = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (Text(item, "type") != "message" || !item.TryGetProperty("content", out var contents) || contents.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var content in contents.EnumerateArray())
            {
                switch (Text(content, "type"))
                {
                    case "refusal":
                        LogFailed(logger, "refused");
                        return null;
                    case "output_text" when Text(content, "text") is { } part:
                        text.Append(part);
                        break;
                }
            }
        }

        var reply = text.ToString().Trim();
        if (reply.Length == 0)
        {
            LogFailed(logger, "no_text");
            return null;
        }

        return reply;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The consultant's supportive reply was not written: {Code}.")]
    private static partial void LogFailed(ILogger logger, string code);
}
