namespace FamilyResilience.Domain.Referrals;

/// <summary>One support-routing rule: a complete score whose tier is one of <see cref="TierCodes"/> routes to the category.</summary>
public sealed record ReferralRoute(string CategoryCode, IReadOnlyList<string> TierCodes);
