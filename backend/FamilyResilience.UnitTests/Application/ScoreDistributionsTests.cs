using FamilyResilience.Application.Analytics;

namespace FamilyResilience.UnitTests.Application;

public sealed class ScoreDistributionsTests
{
    private static TierCount Row(string tier, int count, string ruleSet = "rules_a", string? section = null, int? members = null) =>
        new("instrument", 1, section, ruleSet, 1, tier, count, members ?? count, 0);

    /// <summary>
    /// The rows as the query returns them when no member appears in two tiers of one distribution: each distribution
    /// covers the sum of its tiers' members. With no <c>members</c> given, every result is a different member's.
    /// </summary>
    private static IReadOnlyList<TierCount> Rows(params TierCount[] rows) =>
        rows.Select(row => row with
        {
            DistributionMembers = rows
                .Where(other => (other.RuleSetCode, other.SectionCode) == (row.RuleSetCode, row.SectionCode))
                .Sum(other => other.Members),
        }).ToList();

    [Fact]
    public void Each_rule_set_gets_its_own_distribution_with_tiers_in_code_order()
    {
        var built = ScoreDistributions.Build(Rows(Row("tier_b", 12, "rules_b"), Row("tier_c", 15), Row("tier_a", 20)), 10);

        Assert.Equal(2, built.Count);
        Assert.Equal(("rules_a", (int?)35, false), (built[0].RuleSetCode, built[0].Total, built[0].Suppressed));
        Assert.Equal([new TierCountResponse("tier_a", 20, false), new TierCountResponse("tier_c", 15, false)], built[0].Tiers);
        Assert.Equal(("rules_b", (int?)12, false), (built[1].RuleSetCode, built[1].Total, built[1].Suppressed));
    }

    [Fact]
    public void Module_distributions_of_one_version_are_kept_apart()
    {
        var built = ScoreDistributions.Build(Rows(Row("tier_a", 10, section: "B"), Row("tier_a", 11, section: "A")), 10);

        Assert.Equal(["A", "B"], built.Select(distribution => distribution.SectionCode));
    }

    [Fact]
    public void A_total_under_the_minimum_hides_the_total_and_which_tiers_occur()
    {
        var built = Assert.Single(ScoreDistributions.Build(Rows(Row("tier_a", 6)), 10));

        Assert.Equal(((int?)null, true), (built.Total, built.Suppressed));
        Assert.Empty(built.Tiers);
    }

    [Fact]
    public void A_single_small_tier_hides_the_next_smallest_too()
    {
        var built = Assert.Single(ScoreDistributions.Build(Rows(Row("tier_a", 30), Row("tier_b", 12), Row("tier_c", 4)), 10));

        Assert.Equal(((int?)46, false), (built.Total, built.Suppressed));
        Assert.Equal(
            [new TierCountResponse("tier_a", 30, false), new TierCountResponse("tier_b", null, true), new TierCountResponse("tier_c", null, true)],
            built.Tiers);
    }

    [Fact]
    public void A_minimum_of_one_shows_every_count()
    {
        var built = Assert.Single(ScoreDistributions.Build(Rows(Row("tier_a", 2), Row("tier_b", 1)), 1));

        Assert.Equal(((int?)3, false), (built.Total, built.Suppressed));
        Assert.All(built.Tiers, tier => Assert.False(tier.Suppressed));
    }

    [Fact]
    public void No_results_give_no_distributions() =>
        Assert.Empty(ScoreDistributions.Build([], 10));

    // D-58 counts people: five members scored in two waves have ten results in one tier.
    [Fact]
    public void Ten_results_of_five_members_are_five_people_and_hide_the_whole_distribution()
    {
        var built = Assert.Single(ScoreDistributions.Build(Rows(Row("tier_a", 10, members: 5)), 10));

        Assert.Equal(((int?)null, true), (built.Total, built.Suppressed));
        Assert.Empty(built.Tiers);
    }

    [Fact]
    public void A_tier_with_enough_results_but_too_few_members_is_hidden_with_a_complement()
    {
        var built = Assert.Single(ScoreDistributions.Build(
            Rows(Row("tier_a", 30), Row("tier_b", 24), Row("tier_c", 12, members: 6)), 10));

        Assert.Equal(((int?)66, false), (built.Total, built.Suppressed));
        Assert.Equal(
            [new TierCountResponse("tier_a", 30, false), new TierCountResponse("tier_b", null, true), new TierCountResponse("tier_c", null, true)],
            built.Tiers);
    }

    [Fact]
    public void A_distribution_is_judged_on_its_own_distinct_members_not_on_the_sum_of_its_tiers()
    {
        // Eight members, each scored once in tier_a and once in tier_b in different waves: 16 results, 8 people.
        TierCount[] rows =
        [
            Row("tier_a", 8) with { DistributionMembers = 8 },
            Row("tier_b", 8) with { DistributionMembers = 8 },
        ];

        var built = Assert.Single(ScoreDistributions.Build(rows, 10));

        Assert.Equal(((int?)null, true), (built.Total, built.Suppressed));
        Assert.Empty(built.Tiers);
    }

    [Fact]
    public void The_counts_shown_stay_counts_of_results()
    {
        var built = Assert.Single(ScoreDistributions.Build(Rows(Row("tier_a", 30, members: 15), Row("tier_b", 22, members: 11)), 10));

        Assert.Equal(((int?)52, false), (built.Total, built.Suppressed));
        Assert.Equal([new TierCountResponse("tier_a", 30, false), new TierCountResponse("tier_b", 22, false)], built.Tiers);
    }
}
