namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Rolls tier counts up into one distribution per scoring rule set and scope. A distribution that covers fewer than
/// <c>minimum</c> people is hidden whole, including which tiers occur: a lone listed tier would tell everyone in it
/// apart. Otherwise the tier cells are suppressed like any other breakdown of a published total
/// (<see cref="Suppression"/>), so a hidden tier cannot be recovered by subtraction. People are the distinct members
/// behind the results (D-58): ten results of five members scored in two waves are five people, and are hidden. The
/// counts shown stay counts of results.
/// </summary>
public static class ScoreDistributions
{
    public static IReadOnlyList<ScoreDistributionResponse> Build(IReadOnlyList<TierCount> rows, int minimum)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .GroupBy(row => (row.InstrumentCode, row.VersionNumber, row.SectionCode, row.RuleSetCode, row.RuleSetVersion))
            .OrderBy(group => group.Key.InstrumentCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.VersionNumber)
            .ThenBy(group => group.Key.SectionCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.RuleSetCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.RuleSetVersion)
            .Select(group =>
            {
                var tiers = group.OrderBy(row => row.TierCode, StringComparer.Ordinal).ToList();
                var total = tiers.Sum(row => row.Count);
                var totalHidden = total > 0 && tiers.Max(row => row.DistributionMembers) < minimum;
                var hidden = Suppression.Apply(
                    tiers.Select(row => row.Count).ToList(), tiers.Select(row => row.Members).ToList(), minimum);
                return new ScoreDistributionResponse(
                    group.Key.InstrumentCode,
                    group.Key.VersionNumber,
                    group.Key.SectionCode,
                    group.Key.RuleSetCode,
                    group.Key.RuleSetVersion,
                    totalHidden ? null : total,
                    totalHidden,
                    totalHidden
                        ? []
                        : tiers.Select((row, index) => new TierCountResponse(row.TierCode, hidden[index] ? null : row.Count, hidden[index])).ToList());
            })
            .ToList();
    }
}
