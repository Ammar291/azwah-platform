namespace FamilyResilience.Application.Questionnaires;

/// <summary>The end of a version's effective window (schedule-end and retire).</summary>
public sealed record EffectiveToRequest(DateTimeOffset EffectiveTo);
