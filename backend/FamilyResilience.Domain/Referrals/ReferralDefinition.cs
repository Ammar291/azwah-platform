using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Domain.Referrals;

/// <summary>
/// A parsed <c>referral.v1</c> definition: the scope it routes (the whole version or one section) and its routes. Tier
/// codes and categories come only from the approved rule set; nothing here is a threshold of its own.
/// </summary>
public sealed record ReferralDefinition(string? TargetSectionCode, IReadOnlyList<ReferralRoute> Routes)
{
    /// <summary>
    /// The categories a score routes to, in route order. Only a complete result with a tier routes anywhere: an
    /// incomplete or untiered result is never guessed at.
    /// </summary>
    public IReadOnlyList<string> CategoriesFor(ScoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status != ScoreStatus.Complete || result.TierCode is not { } tier)
        {
            return [];
        }

        return Routes
            .Where(route => route.TierCodes.Contains(tier, StringComparer.Ordinal))
            .Select(route => route.CategoryCode)
            .ToList();
    }
}
