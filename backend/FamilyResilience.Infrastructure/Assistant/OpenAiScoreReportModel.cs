using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Domain.Common;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// <see cref="IScoreReportModel"/> over OpenAI's Responses API (D-112, D-113), on the assistant's settings record and
/// transport (<see cref="OpenAiResponses"/>) with its own model (<see cref="OpenAiSettings.ReportModel"/>, D-116). The
/// answer is asked not to be stored (<c>store: false</c>), and a strict JSON schema shapes it into text fields only. It
/// logs one fixed code when a call goes wrong and never the facts, the writing or anything OpenAI sends back, including
/// its error messages.
/// </summary>
public sealed partial class OpenAiScoreReportModel(OpenAiSettings settings, HttpClient http, ILogger<OpenAiScoreReportModel> logger)
    : IScoreReportModel
{
    /// <summary>
    /// Connections to OpenAI drop now and then (about 3 calls in 60 in live runs) before any answer arrives, and the next
    /// try works, so a dropped connection is tried once more at once. An answer of any status is final: a part's call must
    /// beat the person's next tap, and a pause before a second try would not.
    /// </summary>
    private static readonly OpenAiRetry Retry = new(AfterBusyAnswer: false, TimeSpan.Zero);

    /// <summary>Room for a full report in Arabic, which takes more tokens, plus the model's reasoning.</summary>
    private const int ReportOutputTokens = 8_000;

    /// <summary>Room for one area or a summary; they run as the person answers, so they must stay short.</summary>
    private const int PartOutputTokens = 1_500;

    private const string ThoughtfulEffort = "low";

    /// <summary>No reasoning step: measured about a third faster on short text, and a part's call must beat the person's next tap.</summary>
    private const string FastEffort = "none";

    private static readonly JsonSerializerOptions WritingOptions = new(JsonSerializerDefaults.Web);

    private static readonly object PointSchema = ClosedObject(("title", Text), ("detail", Text));

    private static readonly object ImprovementSchema = ClosedObject(("area", Text), ("reason", Text), ("steps", ListOf(Text)), ("supportCodes", ListOf(Text)));

    private static readonly object CodedImprovementSchema = ClosedObject(("sectionCode", Text), ("area", Text), ("reason", Text), ("steps", ListOf(Text)), ("supportCodes", ListOf(Text)));

    private static readonly object WritingSchema = ClosedObject(
        ("summary", Text),
        ("strengths", ListOf(PointSchema)),
        ("weaknesses", ListOf(PointSchema)),
        ("improvements", ListOf(CodedImprovementSchema)));

    private static readonly object PartSchema = ClosedObject(
        ("strengths", ListOf(PointSchema)),
        ("weaknesses", ListOf(PointSchema)),
        ("improvements", ListOf(ImprovementSchema)));

    private static readonly object SummarySchema = ClosedObject(("summary", Text));

    private static object Text => new { type = "string" };

    public bool IsConfigured => settings.HasKey;

    public string ModelName => settings.ReportModel;

    public Task<Result<ScoreReportWriting>> WriteAsync(ScoreReportFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return SendAsync<ScoreReportWriting>(
            ScoreReportPrompt.Instructions(facts.Language), ScoreReportPrompt.Input(facts), "family_resilience_report", WritingSchema, ThoughtfulEffort, ReportOutputTokens, cancellationToken);
    }

    public Task<Result<ScoreReportPartWriting>> WritePartAsync(ScoreReportPartFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return SendAsync<ScoreReportPartWriting>(
            ScoreReportPrompt.PartInstructions(facts.Language), ScoreReportPrompt.Input(facts), "family_resilience_report_part", PartSchema, FastEffort, PartOutputTokens, cancellationToken);
    }

    public Task<Result<ScoreReportSummaryWriting>> WriteSummaryAsync(ScoreReportSummaryFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return SendAsync<ScoreReportSummaryWriting>(
            ScoreReportPrompt.SummaryInstructions(facts.Language), ScoreReportPrompt.Input(facts), "family_resilience_report_summary", SummarySchema, FastEffort, PartOutputTokens, cancellationToken);
    }

    private async Task<Result<T>> SendAsync<T>(
        string instructions, string input, string schemaName, object schema, string effort, int maxOutputTokens, CancellationToken cancellationToken)
        where T : class
    {
        var body = new
        {
            model = settings.ReportModel,
            instructions,
            input,
            store = false,
            reasoning = new { effort },
            max_output_tokens = maxOutputTokens,
            text = new { format = new { type = "json_schema", name = schemaName, strict = true, schema } },
        };

        using var document = await OpenAiResponses.PostAsync(
            http, settings, JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Retry, code => LogFailed(logger, code), cancellationToken);
        if (document is null)
        {
            return Result.Failure<T>(AssistantErrors.ModelFailed);
        }

        try
        {
            return Read<T>(document.RootElement);
        }
        catch (JsonException)
        {
            return Failed<T>("unreadable");
        }
    }

    /// <summary>The writing from a completed response's message; a refusal or a cut-off answer is a failure.</summary>
    private Result<T> Read<T>(JsonElement root)
        where T : class
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.String
            || status.GetString() != "completed")
        {
            return Failed<T>("not_completed");
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return Failed<T>("no_text");
        }

        foreach (var content in output.EnumerateArray()
                     .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "message")
                     .SelectMany(item => item.GetProperty("content").EnumerateArray()))
        {
            var type = content.GetProperty("type").GetString();
            if (type == "refusal")
            {
                return Failed<T>("refused");
            }

            if (type == "output_text" && content.GetProperty("text").GetString() is { } text
                && JsonSerializer.Deserialize<T>(text, WritingOptions) is { } writing)
            {
                return Result.Success(writing);
            }
        }

        return Failed<T>("no_text");
    }

    private Result<T> Failed<T>(string code)
        where T : class
    {
        LogFailed(logger, code);
        return Result.Failure<T>(AssistantErrors.ModelFailed);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The score report model call failed: {Code}.")]
    private static partial void LogFailed(ILogger logger, string code);

    private static object ListOf(object items) => new { type = "array", items };

    /// <summary>Strict mode needs every object closed and every property listed as required.</summary>
    private static Dictionary<string, object> ClosedObject(params (string Name, object Schema)[] properties) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = properties.Select(property => property.Name).ToArray(),
        ["properties"] = properties.ToDictionary(property => property.Name, property => property.Schema),
    };
}
