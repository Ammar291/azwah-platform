using System.Diagnostics.Metrics;
using System.Globalization;
using FamilyResilience.Application.SupportResources;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.UnitTests.Application.SupportResources;

public sealed class SupportResourceMetricsTests
{
    [Fact]
    public void Searches_are_counted_with_fixed_tags()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        var metrics = new SupportResourceMetrics(factory);
        var counts = new List<(string Instrument, double Value, Dictionary<string, string?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SupportResourceMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => counts.Add(Copy(instrument, value, tags)));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => counts.Add(Copy(instrument, value, tags)));
        listener.Start();

        metrics.SearchCompleted(DiscoveryMode.Deterministic, DiscoveryOutcome.NoMatch, null, TimeSpan.FromMilliseconds(12));
        metrics.SearchCompleted(DiscoveryMode.LanguageModel, DiscoveryOutcome.Matched, DiscoveryFallback.ModelTimeout, TimeSpan.FromMilliseconds(5));
        metrics.ModelCallCompleted(ModelCallResult.Ungrounded);
        metrics.ModelTokensUsed(10, 4);

        var search = counts.Where(c => c.Instrument == "azwah.support_resources.searches").ToList();
        Assert.Equal(2, search.Count);
        Assert.Equal(
            new Dictionary<string, string?> { ["mode"] = "deterministic", ["outcome"] = "no_match", ["fallback"] = "none" },
            search[0].Tags);
        Assert.Equal("model_timeout", search[1].Tags["fallback"]);
        Assert.Equal("language_model", search[1].Tags["mode"]);
        var duration = counts.First(c => c.Instrument == "azwah.support_resources.search_duration");
        Assert.Equal((12d, "deterministic"), (duration.Value, duration.Tags["mode"]));
        Assert.Equal("ungrounded", counts.Single(c => c.Instrument == "azwah.support_resources.model_calls").Tags["result"]);
        var tokens = counts.Where(c => c.Instrument == "azwah.support_resources.model_tokens").ToList();
        Assert.Equal([("input", 10d), ("output", 4d)], tokens.Select(t => (t.Tags["direction"]!, t.Value)));
    }

    private static (string Instrument, double Value, Dictionary<string, string?> Tags) Copy<T>(
        Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var copied = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copied[tag.Key] = tag.Value?.ToString();
        }

        return (instrument.Name, Convert.ToDouble(value, CultureInfo.InvariantCulture), copied);
    }
}
