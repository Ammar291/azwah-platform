using System.Diagnostics.Metrics;
using System.Text.Json;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// Counters and timings for the directory search. Tags are fixed codes only: never search text, terms, categories,
/// resource IDs or subjects. One instance per host, from the host's <see cref="IMeterFactory"/>.
/// </summary>
public sealed class SupportResourceMetrics
{
    public const string MeterName = "FamilyResilience.SupportResources";

    private readonly Counter<long> _searches;
    private readonly Histogram<double> _searchDuration;
    private readonly Counter<long> _modelCalls;
    private readonly Counter<long> _modelTokens;

    public SupportResourceMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _searches = meter.CreateCounter<long>(
            "azwah.support_resources.searches", description: "Directory searches, by mode, outcome and fallback.");
        _searchDuration = meter.CreateHistogram<double>(
            "azwah.support_resources.search_duration", unit: "ms", description: "Time to answer a directory search, by mode.");
        _modelCalls = meter.CreateCounter<long>(
            "azwah.support_resources.model_calls", description: "Attempts to call a language model, by result.");
        _modelTokens = meter.CreateCounter<long>(
            "azwah.support_resources.model_tokens", description: "Language-model tokens used, by direction.");
    }

    public void SearchCompleted(DiscoveryMode mode, DiscoveryOutcome outcome, DiscoveryFallback? fallback, TimeSpan duration)
    {
        var modeCode = Code(mode);
        _searches.Add(
            1,
            new KeyValuePair<string, object?>("mode", modeCode),
            new KeyValuePair<string, object?>("outcome", Code(outcome)),
            new KeyValuePair<string, object?>("fallback", fallback is { } reason ? Code(reason) : "none"));
        _searchDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("mode", modeCode));
    }

    public void ModelCallCompleted(ModelCallResult result) =>
        _modelCalls.Add(1, new KeyValuePair<string, object?>("result", Code(result)));

    public void ModelTokensUsed(int inputTokens, int outputTokens)
    {
        if (inputTokens > 0)
        {
            _modelTokens.Add(inputTokens, new KeyValuePair<string, object?>("direction", "input"));
        }

        if (outputTokens > 0)
        {
            _modelTokens.Add(outputTokens, new KeyValuePair<string, object?>("direction", "output"));
        }
    }

    private static string Code<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
