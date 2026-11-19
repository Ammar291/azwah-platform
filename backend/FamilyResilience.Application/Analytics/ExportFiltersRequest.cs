namespace FamilyResilience.Application.Analytics;

/// <summary>Optional export filters: one wave, one instrument code, or both.</summary>
public sealed record ExportFiltersRequest(Guid? WaveId, string? InstrumentCode);
