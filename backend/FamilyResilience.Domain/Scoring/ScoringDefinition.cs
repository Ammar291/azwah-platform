using System.Text.Json;

namespace FamilyResilience.Domain.Scoring;

/// <summary>A parsed <c>scoring.v1</c> definition. <see cref="Parameters"/> is validated by the named strategy.</summary>
public sealed record ScoringDefinition(string? TargetSectionCode, string PolicyCode, int AlgorithmVersion, JsonElement Parameters);
