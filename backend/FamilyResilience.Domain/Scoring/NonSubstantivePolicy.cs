namespace FamilyResilience.Domain.Scoring;

/// <summary>
/// How non-substantive answers (such as 99) are handled: excluded, and the result is Incomplete when their share of
/// the scored items is greater than <see cref="MaxShare"/>; otherwise the total is the prorated mean, rounded as approved.
/// </summary>
public sealed record NonSubstantivePolicy(decimal MaxShare, int RoundingDecimals, MidpointRounding RoundingMode);
