using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using static FamilyResilience.UnitTests.Domain.DomainTestData;
using static FamilyResilience.UnitTests.Domain.Scoring.ScoringTestContent;

namespace FamilyResilience.UnitTests.Domain.Scoring;

/// <summary>
/// Reference values for <c>summed_item_values</c> version 1, worked out by hand from the policy's definition (sum of
/// item values; with excluded 99 answers, the mean of the substantive answers times the number of items, rounded as
/// the parameters say) and written here as literals. They are never taken from the engine: a failure means the engine
/// and the definition disagree, and the expected value is not the thing to change. The content, bands and parameters
/// are synthetic; no approved rule set uses them.
/// </summary>
public sealed class SummedItemValuesReferenceTests
{
    private const int PreferNotToAnswer = 99;

    private static readonly SummedItemValuesStrategy Strategy = new();

    private static ScoringComputation Score(ScoringContent content, string parametersJson, params int[] answers)
    {
        Assert.Equal(content.Items.Count, answers.Length);
        var responses = content.Items.Select((item, index) => Answer(item, answers[index])).ToArray();
        return Ok(Strategy.Score(new ScoringInput(content, Ok(Strategy.ValidateParameters(Json(parametersJson), content)), responses)));
    }

    private static int[] With99(int count, params int[] substantive) => [.. Enumerable.Repeat(PreferNotToAnswer, count), .. substantive];

    // (a) 13 items, three 99; the other ten sum to 25: mean 2.5 × 13 = 32.5.
    [Theory]
    [InlineData("half_away_from_zero", 33)]
    [InlineData("half_to_even", 32)]
    public void Thirteen_items_with_three_99_and_a_sum_of_25_prorate_to_32_5_which_rounds_by_the_approved_mode(string mode, int expected)
    {
        var computation = Score(
            Module(13, nonSubstantive: true), WithNonSubstantive(maxShare: "0.3", mode: mode, decimals: 0),
            With99(3, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3));

        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Null(computation.IncompleteReason);
        Assert.Equal(expected, computation.TotalScore);
        Assert.Equal(3, computation.Contributions.Count(contribution => contribution.IsExcluded));
        Assert.Equal(13, computation.Contributions.Count);
    }

    // (b) The same shape: a sum of 35 gives 45.5, which both modes round to 46; a sum of 34 gives 44.2, which is 44.
    [Theory]
    [InlineData("half_away_from_zero", new[] { 3, 3, 3, 3, 3, 4, 4, 4, 4, 4 }, 46)]
    [InlineData("half_to_even", new[] { 3, 3, 3, 3, 3, 4, 4, 4, 4, 4 }, 46)]
    [InlineData("half_away_from_zero", new[] { 3, 3, 3, 3, 3, 3, 4, 4, 4, 4 }, 44)]
    [InlineData("half_to_even", new[] { 3, 3, 3, 3, 3, 3, 4, 4, 4, 4 }, 44)]
    public void Thirteen_items_with_three_99_prorate_sums_of_35_and_34_to_46_and_44(string mode, int[] substantive, int expected)
    {
        var computation = Score(
            Module(13, nonSubstantive: true), WithNonSubstantive(maxShare: "0.3", mode: mode, decimals: 0), With99(3, substantive));

        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Equal(expected, computation.TotalScore);
    }

    // (c) 10 items, two 99; the other eight sum to 18: 22.5, on the edge between two synthetic bands.
    [Theory]
    [InlineData("half_away_from_zero", 23, "band_middle")]
    [InlineData("half_to_even", 22, "band_lowest")]
    public void A_prorated_22_5_lands_in_the_band_its_rounding_mode_sends_it_to(string mode, int expectedTotal, string expectedTier)
    {
        var bands = Bands(("band_lowest", 10, 22), ("band_middle", 23, 37), ("band_highest", 38, 50));

        var computation = Score(
            Module(10, nonSubstantive: true), WithNonSubstantive(maxShare: "0.3", mode: mode, decimals: 0, extra: bands),
            With99(2, 2, 2, 2, 2, 2, 2, 3, 3));

        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Equal(expectedTotal, computation.TotalScore);
        Assert.Equal(expectedTier, computation.TierCode);
    }

    // (d) 10 items, one 99; the other nine sum to 28: 28 × 10 / 9 = 31.111…
    [Theory]
    [InlineData(1, "31.1")]
    [InlineData(3, "31.111")]
    public void Nine_answers_summing_28_of_ten_items_prorate_to_31_point_1_recurring_at_the_approved_decimals(int decimals, string expected)
    {
        var computation = Score(
            Module(10, nonSubstantive: true), WithNonSubstantive(maxShare: "0.3", decimals: decimals),
            With99(1, 3, 3, 3, 3, 3, 3, 3, 3, 4));

        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), computation.TotalScore);
        Assert.Null(computation.TierCode);
    }

    // (e) 13 items, three 99; five forward items answered 4 count 4, five reverse items answered 4 count 6 − 4 = 2:
    // 20 + 10 = 30, mean 3 × 13 = 39.
    [Theory]
    [InlineData("half_away_from_zero")]
    [InlineData("half_to_even")]
    public void Reverse_items_are_mirrored_before_the_total_is_prorated(string mode)
    {
        var content = Content(
        [
            .. Enumerable.Range(1, 3).Select(number => Item($"N{number}", nonSubstantive: true)),
            .. Enumerable.Range(1, 5).Select(number => Item($"F{number}", ScoringDirection.Forward, nonSubstantive: true)),
            .. Enumerable.Range(1, 5).Select(number => Item($"R{number}", ScoringDirection.Reverse, nonSubstantive: true)),
        ]);

        var computation = Score(content, WithNonSubstantive(maxShare: "0.3", mode: mode, decimals: 0), With99(3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4));

        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Equal(39, computation.TotalScore);
        Assert.Equal(
            new decimal?[] { null, null, null, 4, 4, 4, 4, 4, 2, 2, 2, 2, 2 },
            computation.Contributions.Select(contribution => contribution.CalculatedValue));
        Assert.All(computation.Contributions.Skip(3), contribution => Assert.Equal(4, contribution.RawValue));
    }

    // (f) A category of four items, one 99 and answers 3, 4, 4: 11 / 3 × 4 = 14.667.
    [Theory]
    [InlineData(0, "15")]
    [InlineData(2, "14.67")]
    public void A_category_with_one_99_prorates_its_own_three_answers_over_its_four_items(int decimals, string expected)
    {
        var content = Content(Enumerable.Range(1, 4).Select(number => Item($"C{number}", nonSubstantive: true, category: "synthetic_category")).ToArray());

        var computation = Score(content, WithNonSubstantive(maxShare: "0.3", decimals: decimals), PreferNotToAnswer, 3, 4, 4);

        var category = Assert.Single(computation.CategoryScores);
        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Equal(("synthetic_category", 4), (category.CategoryCode, category.ItemCount));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), category.Score);
    }

    // (g) The share allowed is inclusive: 3 of 10 is exactly 0.3 and still scores (seven answers of 3: mean 3 × 10 = 30).
    [Fact]
    public void Three_of_ten_answers_excluded_at_a_maximum_share_of_0_3_is_still_complete()
    {
        var computation = Score(Module(10, nonSubstantive: true), WithNonSubstantive(maxShare: "0.3"), With99(3, 3, 3, 3, 3, 3, 3, 3));

        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Null(computation.IncompleteReason);
        Assert.Equal(30, computation.TotalScore);
    }

    // (g) 4 of 13 is 0.3077 and 4 of 10 is 0.4: both over 0.3, so there is no total and no tier.
    [Theory]
    [InlineData(13)]
    [InlineData(10)]
    public void Four_answers_excluded_of_thirteen_or_of_ten_is_over_a_maximum_share_of_0_3_and_incomplete(int itemCount)
    {
        var computation = Score(
            Module(itemCount, nonSubstantive: true), WithNonSubstantive(maxShare: "0.3"),
            With99(4, [.. Enumerable.Repeat(3, itemCount - 4)]));

        Assert.Equal(ScoreStatus.Incomplete, computation.Status);
        Assert.Equal(ScoreIncompleteReasons.NonSubstantiveShareExceeded, computation.IncompleteReason);
        Assert.Equal("non_substantive_share_exceeded", computation.IncompleteReason);
        Assert.Null(computation.TotalScore);
        Assert.Null(computation.TierCode);
        Assert.Empty(computation.CategoryScores);
        Assert.Equal(4, computation.Contributions.Count(contribution => contribution.IsExcluded));
    }
}
