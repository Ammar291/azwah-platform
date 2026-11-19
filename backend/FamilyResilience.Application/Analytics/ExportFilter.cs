using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Analytics;

/// <summary>The only filters an export accepts: one wave, one instrument, or neither.</summary>
public sealed record ExportFilter(WaveId? WaveId, string? InstrumentCode);
