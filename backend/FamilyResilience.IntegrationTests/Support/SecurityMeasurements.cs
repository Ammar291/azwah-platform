using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FamilyResilience.Api.Observability;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// Records the security counters of one test host only: meters from that host's <see cref="IMeterFactory"/>, so requests
/// that other tests send to other hosts at the same time are not counted.
/// </summary>
internal sealed class SecurityMeasurements : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Instrument, long Value, Dictionary<string, string?> Tags)> _measurements = new();

    public SecurityMeasurements(WebApplicationFactory<Program> host)
    {
        var meterFactory = host.Services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SecurityMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, meterFactory))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var copied = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copied[tag.Key] = tag.Value?.ToString();
            }

            _measurements.Enqueue((instrument.Name, value, copied));
        });
        _listener.Start();
    }

    /// <summary>The sum recorded on <paramref name="instrument"/> with every one of <paramref name="tags"/>.</summary>
    public long Total(string instrument, params (string Key, string Value)[] tags) =>
        _measurements
            .Where(measurement => measurement.Instrument == instrument
                && tags.All(tag => measurement.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value))
            .Sum(measurement => measurement.Value);

    public void Dispose() => _listener.Dispose();
}
