using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.UnitTests.Domain.Scoring;

/// <summary>The points an answer counts: one rule for the score and for what a report model is told (D-113).</summary>
public sealed class ScoringItemPointsTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    public void A_forward_item_counts_the_raw_value(int raw, int points) =>
        Assert.Equal(points, ScoringTestContent.Item("Q01", ScoringDirection.Forward, scaleSize: 4).PointsFor(raw));

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    [InlineData(4, 1)]
    public void A_reverse_item_mirrors_the_scale(int raw, int points) =>
        Assert.Equal(points, ScoringTestContent.Item("Q02", ScoringDirection.Reverse, scaleSize: 4).PointsFor(raw));
}
