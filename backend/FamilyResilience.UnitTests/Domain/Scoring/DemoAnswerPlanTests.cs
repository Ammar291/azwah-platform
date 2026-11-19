using CsCheck;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using static FamilyResilience.UnitTests.Domain.DomainTestData;
using static FamilyResilience.UnitTests.Domain.Scoring.ScoringTestContent;

namespace FamilyResilience.UnitTests.Domain.Scoring;

public sealed class DemoAnswerPlanTests
{
    private static readonly SummedItemValuesStrategy Strategy = new();

    private static SummedItemValuesParameters Parameters(ScoringContent content, string json = Minimal) =>
        (SummedItemValuesParameters)Ok(Strategy.ValidateParameters(Json(json), content));

    private static ScoringComputation ScoreOf(ScoringContent content, SummedItemValuesParameters parameters, DemoAnswerPlan plan) =>
        Ok(Strategy.Score(new ScoringInput(content, parameters, plan.Answers)));

    /// <summary>39 items on 1–4, every third one reversed, with three synthetic bands over 39–156.</summary>
    private static (ScoringContent Content, SummedItemValuesParameters Parameters) ThreeBands()
    {
        var content = Content(Enumerable.Range(1, 39)
            .Select(number => Item($"Q{number:00}", number % 3 == 0 ? ScoringDirection.Reverse : ScoringDirection.Forward, scaleSize: 4))
            .ToArray());
        return (content, Parameters(content, Minimal[..^1] + Bands(("band_low", 39, 78), ("band_middle", 79, 117), ("band_high", 118, 156)) + "}"));
    }

    [Theory]
    [InlineData(DemoScoreLevel.Low, "band_low", 58)]
    [InlineData(DemoScoreLevel.Medium, "band_middle", 98)]
    [InlineData(DemoScoreLevel.High, "band_high", 137)]
    public void Each_level_scores_in_its_band_at_the_bands_midpoint(DemoScoreLevel level, string tierCode, int total)
    {
        var (content, parameters) = ThreeBands();

        var plan = DemoAnswerPlan.Create(content, parameters, level);
        var computation = ScoreOf(content, parameters, plan);

        Assert.Equal((total, tierCode), (plan.Total, plan.TierCode));
        Assert.Equal((ScoreStatus.Complete, (decimal?)total, tierCode), (computation.Status, computation.TotalScore, computation.TierCode));
    }

    [Fact]
    public void Reverse_items_are_answered_on_the_mirrored_side_of_the_scale()
    {
        var forward = Item("Q01", ScoringDirection.Forward, scaleSize: 4);
        var reverse = Item("Q02", ScoringDirection.Reverse, scaleSize: 4);
        var content = Content(forward, reverse);

        var high = DemoAnswerPlan.Create(content, Parameters(content), DemoScoreLevel.High);

        Assert.Equal([4, 1], high.Answers.Select(answer => answer.RawValue));
        Assert.Equal(8, high.Total);
    }

    [Fact]
    public void Medium_uses_the_band_that_holds_the_middle_of_the_possible_totals()
    {
        var content = Content(Item("Q01", scaleSize: 3), Item("Q02", scaleSize: 3));
        var parameters = Parameters(content, Minimal[..^1] + Bands(("band_low", 2, 3), ("band_high", 4, 6)) + "}");

        var plan = DemoAnswerPlan.Create(content, parameters, DemoScoreLevel.Medium);

        Assert.Equal(("band_high", 5), (plan.TierCode, plan.Total));
    }

    [Theory]
    [InlineData(DemoScoreLevel.Low, 10)]
    [InlineData(DemoScoreLevel.Medium, 30)]
    [InlineData(DemoScoreLevel.High, 50)]
    public void Without_bands_the_levels_are_the_lowest_middle_and_highest_totals(DemoScoreLevel level, int total)
    {
        var content = Module(10);

        var plan = DemoAnswerPlan.Create(content, Parameters(content), level);

        Assert.Equal((total, (string?)null), (plan.Total, plan.TierCode));
    }

    [Fact]
    public void Every_item_is_answered_and_a_non_substantive_answer_is_never_chosen()
    {
        var unscored = ItemWithValues("Q00", [1, 2, 3], ScoringDirection.NotScored, nonSubstantive: true);
        var scored = Enumerable.Range(1, 5).Select(number => Item($"Q{number:00}", scaleSize: 5, nonSubstantive: true)).ToList();
        var content = Content([unscored, .. scored]);
        var parameters = Parameters(content, WithNonSubstantive());

        foreach (var level in Enum.GetValues<DemoScoreLevel>())
        {
            var plan = DemoAnswerPlan.Create(content, parameters, level);

            Assert.Equal(content.Items.Select(item => item.ItemId), plan.Answers.Select(answer => answer.ItemId));
            Assert.All(plan.Answers, answer => Assert.NotEqual(99, answer.RawValue));
            Assert.Equal(1, plan.Answers[0].RawValue);
        }
    }

    [Fact]
    public void Extra_points_are_spread_across_the_questionnaire_rather_than_piled_at_the_start()
    {
        var content = Module(10, scaleSize: 4);

        var plan = DemoAnswerPlan.Create(content, Parameters(content), DemoScoreLevel.Medium);

        Assert.Equal(25, plan.Total);
        Assert.Equal([2, 3, 2, 3, 2, 3, 2, 3, 2, 3], plan.Answers.Select(answer => answer.RawValue));
    }

    [Fact]
    public void Any_scope_and_banding_scores_each_level_in_the_band_the_plan_names() =>
        (from scaleSize in Gen.Int[2, 7]
         from reversed in Gen.Bool.Array[1, 12]
         from firstCut in Gen.Double[0, 1]
         from secondCut in Gen.Double[0, 1]
         select (scaleSize, reversed, firstCut, secondCut))
        .Sample(sample =>
        {
            var content = Content(sample.reversed
                .Select((isReverse, index) => Item($"Q{index:00}", isReverse ? ScoringDirection.Reverse : ScoringDirection.Forward, sample.scaleSize))
                .ToArray());
            var parameters = Parameters(content, Minimal[..^1] + Bands(Banding(content, sample.firstCut, sample.secondCut)) + "}");

            foreach (var level in Enum.GetValues<DemoScoreLevel>())
            {
                var plan = DemoAnswerPlan.Create(content, parameters, level);
                var computation = ScoreOf(content, parameters, plan);

                Assert.Equal(((decimal?)plan.Total, plan.TierCode), (computation.TotalScore, computation.TierCode));
            }

            Assert.Equal(parameters.TierBands[0].Code, DemoAnswerPlan.Create(content, parameters, DemoScoreLevel.Low).TierCode);
            Assert.Equal(parameters.TierBands[^1].Code, DemoAnswerPlan.Create(content, parameters, DemoScoreLevel.High).TierCode);
        });

    /// <summary>One to three contiguous bands covering the scope's totals, cut at the given fractions of its range.</summary>
    private static (string Code, int Min, int Max)[] Banding(ScoringContent content, double firstCut, double secondCut)
    {
        var minimum = content.MinimumTotal();
        var maximum = content.MaximumTotal();
        var cuts = new[] { firstCut, secondCut }
            .Select(cut => minimum + (int)Math.Floor(cut * (maximum - minimum)))
            .Where(cut => cut < maximum)
            .Distinct()
            .Order()
            .ToList();
        var starts = new List<int> { minimum };
        starts.AddRange(cuts.Select(cut => cut + 1));
        return starts.Select((start, index) => ($"band_{index}", start, index + 1 < starts.Count ? starts[index + 1] - 1 : maximum)).ToArray();
    }
}
