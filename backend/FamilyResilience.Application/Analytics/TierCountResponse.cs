namespace FamilyResilience.Application.Analytics;

/// <summary>Results in one tier. When <c>suppressed</c>, the count is null.</summary>
public sealed record TierCountResponse(string Tier, int? Count, bool Suppressed);
