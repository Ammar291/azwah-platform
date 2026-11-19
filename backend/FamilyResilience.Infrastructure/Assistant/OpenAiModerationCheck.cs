using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyResilience.Application.Assistant;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// <see cref="IAssistantSafetyCheck"/> over OpenAI's moderation endpoint (D-126, D-136). Any self-harm category flagged
/// is <see cref="AssistantSafetyVerdict.DangerToSelf"/>, which wins; otherwise a violence or threat category scored at
/// <see cref="ViolenceThreshold"/> or above is <see cref="AssistantSafetyVerdict.DangerOfViolence"/>. Either way the reply
/// offers the SOS screen whatever the model wrote. It never throws (a failure is
/// <see cref="AssistantSafetyVerdict.NotChecked"/>), and logs one fixed code, never the text or the answer.
/// </summary>
public sealed partial class OpenAiModerationCheck(OpenAiSettings settings, HttpClient http, ILogger<OpenAiModerationCheck> logger)
    : IAssistantSafetyCheck
{
    private const string ModerationsAddress = "https://api.openai.com/v1/moderations";

    /// <summary>
    /// A parent describing a quarrel or a rough game should not get an SOS button; a description of being hurt should
    /// (engineering call, tuned on the evaluation set, D-127).
    /// </summary>
    public const double ViolenceThreshold = 0.6;

    private static readonly string[] SelfHarm = ["self-harm", "self-harm/intent", "self-harm/instructions"];

    private static readonly string[] Violence = ["violence", "violence/graphic", "harassment/threatening", "hate/threatening"];

    public bool IsConfigured => settings.HasKey;

    public async Task<AssistantSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return AssistantSafetyVerdict.Clear;
        }

        using var document = await PostAsync(JsonValue.Create(text), cancellationToken);
        return document is null ? AssistantSafetyVerdict.NotChecked : Verdict(document.RootElement);
    }

    /// <summary>
    /// The texts in one request (D-141: a call's earlier conversation): OpenAI answers one result per input, in order. A
    /// failure, or an answer that does not hold one result per text, is <see cref="AssistantSafetyVerdict.NotChecked"/>
    /// for every text.
    /// </summary>
    public async Task<IReadOnlyList<AssistantSafetyVerdict>> CheckManyAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return [];
        }

        var verdicts = Enumerable.Repeat(AssistantSafetyVerdict.Clear, texts.Count).ToArray();
        var sent = Enumerable.Range(0, texts.Count).Where(index => !string.IsNullOrWhiteSpace(texts[index])).ToList();
        if (sent.Count == 0)
        {
            return verdicts;
        }

        using var document = await PostAsync(new JsonArray(sent.Select(index => (JsonNode?)JsonValue.Create(texts[index])).ToArray()), cancellationToken);
        if (document is null
            || !document.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() != sent.Count)
        {
            return Enumerable.Repeat(AssistantSafetyVerdict.NotChecked, texts.Count).ToArray();
        }

        var position = 0;
        foreach (var result in results.EnumerateArray())
        {
            verdicts[sent[position++]] = Verdict([result]);
        }

        return verdicts;
    }

    internal static AssistantSafetyVerdict Verdict(JsonElement root)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return AssistantSafetyVerdict.NotChecked;
        }

        return Verdict([.. results.EnumerateArray()]);
    }

    /// <summary>The verdict over <paramref name="results"/>: self-harm in any of them wins (D-136).</summary>
    private static AssistantSafetyVerdict Verdict(IReadOnlyList<JsonElement> results)
    {
        var toSelf = false;
        var violence = false;
        foreach (var result in results)
        {
            var categories = result.TryGetProperty("categories", out var flags) && flags.ValueKind == JsonValueKind.Object ? flags : default;
            var scores = result.TryGetProperty("category_scores", out var values) && values.ValueKind == JsonValueKind.Object ? values : default;
            toSelf |= SelfHarm.Any(name => Flag(categories, name));
            violence |= Violence.Any(name => Flag(categories, name) && Score(scores, name) >= ViolenceThreshold);
        }

        // Self-harm takes priority (D-136): the person themselves may be in danger.
        return toSelf ? AssistantSafetyVerdict.DangerToSelf
            : violence ? AssistantSafetyVerdict.DangerOfViolence
            : AssistantSafetyVerdict.Clear;
    }

    /// <summary>The parsed answer for <paramref name="input"/>, or null when the call failed (one fixed code logged).</summary>
    private async Task<JsonDocument?> PostAsync(JsonNode? input, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ModerationsAddress)
            {
                Content = new StringContent(
                    new JsonObject { ["model"] = settings.ModerationModel, ["input"] = input }.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, $"status_{(int)response.StatusCode}");
                return null;
            }

            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            LogFailed(logger, exception is OperationCanceledException ? "cancelled" : "failed");
            return null;
        }
    }

    private static bool Flag(JsonElement categories, string name) =>
        categories.ValueKind == JsonValueKind.Object && categories.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static double Score(JsonElement scores, string name) =>
        scores.ValueKind == JsonValueKind.Object && scores.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The assistant safety check did not run: {Code}.")]
    private static partial void LogFailed(ILogger logger, string code);
}
