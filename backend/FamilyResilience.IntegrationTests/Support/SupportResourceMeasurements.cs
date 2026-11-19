using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using FamilyResilience.Application.SupportResources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// Records the support-resource instruments of one test host only: meters from that host's <see cref="IMeterFactory"/>,
/// so searches that other tests send to other hosts at the same time are not counted.
/// </summary>
internal sealed class SupportResourceMeasurements : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Instrument, double Value, Dictionary<string, string?> Tags)> _measurements = new();

    public SupportResourceMeasurements(WebApplicationFactory<Program> host)
    {
        var meterFactory = host.Services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SupportResourceMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, meterFactory))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public IReadOnlyList<(string Instrument, double Value, Dictionary<string, string?> Tags)> All => [.. _measurements];

    public double Total(string instrument, params (string Key, string Value)[] tags) =>
        _measurements
            .Where(measurement => measurement.Instrument == instrument
                && tags.All(tag => measurement.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value))
            .Sum(measurement => measurement.Value);

    public void Dispose() => _listener.Dispose();

    private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var copied = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copied[tag.Key] = tag.Value?.ToString();
        }

        _measurements.Enqueue((instrument.Name, Convert.ToDouble(value, CultureInfo.InvariantCulture), copied));
    }
}
