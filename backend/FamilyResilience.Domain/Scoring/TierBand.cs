namespace FamilyResilience.Domain.Scoring;

/// <summary>An approved band of totals, both bounds included.</summary>
public sealed record TierBand(string Code, int Min, int Max);
