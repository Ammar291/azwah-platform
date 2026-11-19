using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using static FamilyResilience.UnitTests.Domain.DomainTestData;
using static FamilyResilience.UnitTests.Domain.Scoring.ScoringTestContent;

namespace FamilyResilience.UnitTests.Domain.Scoring;

/// <summary>The range a rule set's totals can take and its approved bands, as the trend chart draws them (D-132).</summary>
public sealed class ScoreScaleTests
{
    [Fact]
    public void A_total_with_bands_runs_from_the_lowest_to_the_highest_total_with_the_bands_in_order()
    {
        var content = Module(3);
        var parameters = Ok(SummedItemValuesParameters.Read(
            Json(Minimal[..^1] + Bands(("band_high", 10, 15), ("band_low", 3, 9)) + "}"), content));

        var scale = parameters.TotalScale(content);

        Assert.NotNull(scale);
        Assert.Equal((3, 15), (scale.Minimum, scale.Maximum));
        Assert.Equal([new TierBand("band_low", 3, 9), new TierBand("band_high", 10, 15)], scale.Bands);
    }

    [Fact]
    public void A_total_without_bands_has_the_range_and_no_bands()
    {
        var content = Content(Item("Q01", scaleSize: 4), Item("Q02", ScoringDirection.Reverse, scaleSize: 4), Item("Q03", ScoringDirection.NotScored));
        var parameters = Ok(SummedItemValuesParameters.Read(Json(Minimal), content));

        var scale = parameters.TotalScale(content);

        Assert.NotNull(scale);
        Assert.Equal((2, 8), (scale.Minimum, scale.Maximum));
        Assert.Empty(scale.Bands);
    }

    [Fact]
    public void Without_a_total_there_is_no_scale()
    {
        var content = Content(Item("Q01", category: "finance"), Item("Q02", category: "finance"));
        var parameters = Ok(SummedItemValuesParameters.Read(
            Json("""{"reverseScoring":"mirror_scale","unanswered":"reject","total":false}"""), content));

        Assert.Null(parameters.TotalScale(content));
    }

    [Fact]
    public void Scales_match_only_with_the_same_range_and_the_same_bands()
    {
        var scale = new ScoreScale(39, 156, [new TierBand("low", 39, 78), new TierBand("high", 79, 156)]);

        Assert.True(scale.Matches(new ScoreScale(39, 156, [new TierBand("low", 39, 78), new TierBand("high", 79, 156)])));
        Assert.False(scale.Matches(new ScoreScale(39, 155, [new TierBand("low", 39, 78), new TierBand("high", 79, 155)])));
        Assert.False(scale.Matches(new ScoreScale(39, 156, [new TierBand("low", 39, 80), new TierBand("high", 81, 156)])));
        Assert.False(scale.Matches(new ScoreScale(39, 156, [new TierBand("lower", 39, 78), new TierBand("high", 79, 156)])));
        Assert.False(scale.Matches(new ScoreScale(39, 156, [])));
    }
}
