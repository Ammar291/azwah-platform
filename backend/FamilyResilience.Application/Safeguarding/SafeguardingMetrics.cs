using System.Diagnostics.Metrics;
using System.Text.Json;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Counters for safety alerts (D-135), meter <see cref="MeterName"/>. Tags are fixed snake_case codes only: never text,
/// a household, a member, a user or an alert ID. One instance per host, from the host's <see cref="IMeterFactory"/>.
/// </summary>
public sealed class SafeguardingMetrics
{
    public const string MeterName = "FamilyResilience.Safeguarding";

    private readonly Counter<long> _alertsRaised;
    private readonly Counter<long> _alertsRepeated;
    private readonly Counter<long> _recordingFailures;
    private readonly Counter<long> _recordingsSkipped;

    public SafeguardingMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _alertsRaised = meter.CreateCounter<long>(
            "azwah.safeguarding.alerts_raised", description: "New safety alerts, by concern, severity and source.");
        _alertsRepeated = meter.CreateCounter<long>(
            "azwah.safeguarding.alerts_repeated",
            description: "Signals folded into an unresolved safety alert (re-opening an acknowledged one), by concern, severity and source.");
        _recordingFailures = meter.CreateCounter<long>(
            "azwah.safeguarding.recording_failures", description: "Safety signals that could not be recorded, by source.");
        _recordingsSkipped = meter.CreateCounter<long>(
            "azwah.safeguarding.recording_skipped",
            description: "Safety signals not recorded because the server has no record of the caller, by source.");
    }

    /// <summary>A new alert; a repeat folded into an unresolved one is counted by <see cref="AlertRepeated"/>.</summary>
    public void AlertRaised(SafetyConcern concern, SafetySeverity severity, SafetyAlertSource source) =>
        _alertsRaised.Add(1, Tags(concern, severity, source));

    /// <summary>A repeat folded into an unresolved alert, so a repeat pages as a new alert does.</summary>
    public void AlertRepeated(SafetyConcern concern, SafetySeverity severity, SafetyAlertSource source) =>
        _alertsRepeated.Add(1, Tags(concern, severity, source));

    public void RecordingFailed(SafetyAlertSource source) =>
        _recordingFailures.Add(1, new KeyValuePair<string, object?>("source", Code(source)));

    /// <summary>A caller's signal with nobody to record it for (the server has no platform user for the caller).</summary>
    public void RecordingSkipped(SafetyAlertSource source) =>
        _recordingsSkipped.Add(1, new KeyValuePair<string, object?>("source", Code(source)));

    private static KeyValuePair<string, object?>[] Tags(SafetyConcern concern, SafetySeverity severity, SafetyAlertSource source) =>
    [
        new("concern", Code(concern)),
        new("severity", Code(severity)),
        new("source", Code(source)),
    ];

    private static string Code<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
