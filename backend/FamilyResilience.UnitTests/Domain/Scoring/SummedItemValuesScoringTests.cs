using System.Text.Json;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using static FamilyResilience.UnitTests.Domain.DomainTestData;
using static FamilyResilience.UnitTests.Domain.Scoring.ScoringTestContent;

namespace FamilyResilience.UnitTests.Domain.Scoring;

public sealed class SummedItemValuesScoringTests
{
    private static readonly SummedItemValuesStrategy Strategy = new();

    private static Result<ScoringComputation> TryScore(ScoringContent content, string parametersJson, params ScoringResponse[] responses) =>
        Strategy.Score(new ScoringInput(content, Ok(Strategy.ValidateParameters(Json(parametersJson), content)), responses));

    private static ScoringComputation Score(ScoringContent content, string parametersJson, params ScoringResponse[] responses) =>
        Ok(TryScore(content, parametersJson, responses));

    private static ScoringResponse[] AnswerAll(ScoringContent content, params int[] values) =>
        content.Items.Select((item, index) => Answer(item, values[index])).ToArray();

    [Fact]
    public void Forward_items_contribute_their_raw_values_and_sum_to_the_total()
    {
        var content = Module(3);

        var computation = Score(content, Minimal, AnswerAll(content, 2, 3, 4));

        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Equal(9m, computation.TotalScore);
        Assert.Equal(new decimal?[] { 2, 3, 4 }, computation.Contributions.Select(contribution => contribution.CalculatedValue));
        Assert.Equal(SummedItemValuesStrategy.Code, computation.PolicyCode);
        Assert.Equal(1, computation.AlgorithmVersion);
    }

    [Theory]
    [InlineData(4, 1, 4)]
    [InlineData(4, 2, 3)]
    [InlineData(4, 3, 2)]
    [InlineData(4, 4, 1)]
    [InlineData(5, 1, 5)]
    [InlineData(5, 3, 3)]
    [InlineData(5, 5, 1)]
    public void A_reverse_scored_item_mirrors_its_scale(int scaleSize, int raw, int expected)
    {
        var item = Item("Q01", ScoringDirection.Reverse, scaleSize);

        var contribution = Assert.Single(Score(Content(item), Minimal, Answer(item, raw)).Contributions);

        Assert.Equal(expected, contribution.CalculatedValue);
        Assert.Equal(raw, contribution.RawValue);
    }

    [Fact]
    public void Reverse_scoring_applies_only_to_items_marked_reverse()
    {
        var content = Content(Item("Q01"), Item("Q02", ScoringDirection.Reverse));

        var computation = Score(content, Minimal, AnswerAll(content, 1, 1));

        Assert.Equal(new decimal?[] { 1, 5 }, computation.Contributions.Select(contribution => contribution.CalculatedValue));
        Assert.Equal(6m, computation.TotalScore);
    }

    [Fact]
    public void Items_that_are_not_scored_are_ignored_even_when_answered()
    {
        var content = Content(Item("Q01"), Item("Q02", ScoringDirection.NotScored));

        var computation = Score(content, Minimal, AnswerAll(content, 2, 5));

        Assert.Single(computation.Contributions);
        Assert.Equal(2m, computation.TotalScore);
    }

    [Fact]
    public void An_item_without_an_approved_direction_stops_scoring()
    {
        var content = Content(Item("Q01"), Item("Q02", ScoringDirection.Unspecified));
        var parameters = new SummedItemValuesParameters(true, null, []);

        var scored = Strategy.Score(new ScoringInput(content, parameters, AnswerAll(content, 1, 1)));

        Assert.Equal(ScoringErrors.ItemDirectionUnspecified.Code, scored.Error?.Code);
    }

    [Fact]
    public void An_unanswered_scored_item_stops_scoring_and_is_named()
    {
        var content = Module(3);

        var scored = TryScore(content, Minimal, Answer(content.Items[0], 1), Answer(content.Items[1], 1));

        Assert.Equal(ScoringErrors.UnansweredItems.Code, scored.Error?.Code);
        Assert.Contains("Q03", scored.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_with_another_items_option_is_refused()
    {
        var content = Module(2);
        var foreign = new ScoringResponse(content.Items[0].ItemId, content.Items[1].Options[1].OptionId, 2);

        Assert.Equal(ScoringErrors.AnswerInvalid.Code, TryScore(content, Minimal, foreign, Answer(content.Items[1], 1)).Error?.Code);
    }

    [Fact]
    public void An_answer_whose_raw_value_differs_from_its_option_is_refused()
    {
        var content = Module(1);
        var tampered = new ScoringResponse(content.Items[0].ItemId, content.Items[0].Options[0].OptionId, 5);

        Assert.Equal(ScoringErrors.AnswerInvalid.Code, TryScore(content, Minimal, tampered).Error?.Code);
    }

    [Fact]
    public void An_answer_for_an_item_outside_the_scope_is_refused()
    {
        var content = Module(1);
        var outside = new ScoringResponse(InstrumentItemId.New(), ResponseOptionId.New(), 1);

        Assert.Equal(ScoringErrors.AnswerInvalid, TryScore(content, Minimal, Answer(content.Items[0], 1), outside).Error);
    }

    [Fact]
    public void A_99_answer_is_never_added_as_99()
    {
        var content = Module(10, nonSubstantive: true);

        var computation = Score(content, WithNonSubstantive("0.3"), AnswerAll(content, 99, 3, 3, 3, 3, 3, 3, 3, 3, 3));

        var declined = computation.Contributions[0];
        Assert.True(declined.IsExcluded);
        Assert.Null(declined.CalculatedValue);
        Assert.Equal(99, declined.RawValue);
        Assert.Equal(30m, computation.TotalScore);
    }

    [Theory]
    [InlineData(10, 3, ScoreStatus.Complete)]
    [InlineData(10, 4, ScoreStatus.Incomplete)]
    [InlineData(13, 3, ScoreStatus.Complete)]
    [InlineData(13, 4, ScoreStatus.Incomplete)]
    public void More_than_the_approved_share_of_99_answers_makes_the_result_incomplete(int items, int declined, ScoreStatus expected)
    {
        var content = Module(items, nonSubstantive: true);

        var computation = Score(
            content, WithNonSubstantive("0.3"), content.Items.Select((item, index) => Answer(item, index < declined ? 99 : 3)).ToArray());

        Assert.Equal(expected, computation.Status);
        Assert.Equal(declined, computation.Contributions.Count(contribution => contribution.IsExcluded));
        if (expected == ScoreStatus.Incomplete)
        {
            Assert.Equal(ScoreIncompleteReasons.NonSubstantiveShareExceeded, computation.IncompleteReason);
            Assert.Null(computation.TotalScore);
            Assert.Null(computation.TierCode);
            Assert.Empty(computation.CategoryScores);
        }
    }

    [Fact]
    public void A_max_share_of_zero_makes_any_99_answer_incomplete()
    {
        var content = Module(10, nonSubstantive: true);

        var computation = Score(content, WithNonSubstantive("0"), AnswerAll(content, 99, 3, 3, 3, 3, 3, 3, 3, 3, 3));

        Assert.Equal(ScoreStatus.Incomplete, computation.Status);
    }

    [Theory]
    [InlineData("half_away_from_zero", 33)]
    [InlineData("half_to_even", 32)]
    public void Prorated_totals_round_as_approved(string mode, int expected)
    {
        // 10 items, 2 declined, 8 answers summing 26: 26 × 10 / 8 = 32.5.
        var content = Module(10, nonSubstantive: true);

        var computation = Score(content, WithNonSubstantive("0.3", mode), AnswerAll(content, 99, 99, 3, 3, 3, 3, 3, 3, 4, 4));

        Assert.Equal(expected, computation.TotalScore);
    }

    [Fact]
    public void Prorated_totals_keep_the_approved_number_of_decimals()
    {
        // 9 answers summing 28 of 10 items: 28 × 10 / 9 = 31.111…
        var content = Module(10, nonSubstantive: true);

        var computation = Score(content, WithNonSubstantive("0.3", decimals: 2), AnswerAll(content, 99, 3, 3, 3, 3, 3, 3, 3, 3, 4));

        Assert.Equal(31.11m, computation.TotalScore);
    }

    [Fact]
    public void Category_scores_sum_their_items_and_the_total_sums_every_item()
    {
        var content = Content(
            Item("Q01", category: "finance"), Item("Q02", category: "finance"), Item("Q03", category: "debt"), Item("Q04"));

        var computation = Score(content, Minimal, AnswerAll(content, 1, 2, 4, 5));

        Assert.Equal(12m, computation.TotalScore);
        Assert.Equal(
            new[] { ("debt", 4m, 1), ("finance", 3m, 2) },
            computation.CategoryScores.Select(score => (score.CategoryCode, score.Score, score.ItemCount)));
    }

    [Fact]
    public void Without_a_total_the_result_has_categories_but_no_total_or_tier()
    {
        var content = Content(Item("Q01", category: "finance"), Item("Q02", category: "finance"));

        var computation = Score(
            content, """{"reverseScoring":"mirror_scale","unanswered":"reject","total":false}""", AnswerAll(content, 2, 3));

        Assert.Null(computation.TotalScore);
        Assert.Null(computation.TierCode);
        Assert.Equal(5m, Assert.Single(computation.CategoryScores).Score);
    }

    [Theory]
    [InlineData(new[] { 1, 1, 1 }, "band_low")]
    [InlineData(new[] { 1, 2, 3 }, "band_low")]
    [InlineData(new[] { 2, 2, 3 }, "band_mid")]
    [InlineData(new[] { 4, 4, 4 }, "band_mid")]
    [InlineData(new[] { 4, 4, 5 }, "band_high")]
    [InlineData(new[] { 5, 5, 5 }, "band_high")]
    public void Tier_bands_include_both_of_their_bounds(int[] answers, string tier)
    {
        // Synthetic bands for three 1–5 items (totals 3–15): 3–6, 7–12, 13–15. Approved only inside this test.
        var content = Module(3);
        var parameters = Minimal[..^1] + Bands(("band_low", 3, 6), ("band_mid", 7, 12), ("band_high", 13, 15)) + "}";

        Assert.Equal(tier, Score(content, parameters, AnswerAll(content, answers)).TierCode);
    }

    [Fact]
    public void Without_bands_no_tier_is_given()
    {
        var content = Module(3);

        Assert.Null(Score(content, Minimal, AnswerAll(content, 5, 5, 5)).TierCode);
    }

    [Fact]
    public void Scoring_the_same_answers_twice_gives_the_same_result()
    {
        var content = Module(10, nonSubstantive: true);
        var answers = AnswerAll(content, 99, 1, 2, 3, 4, 5, 1, 2, 3, 4);

        var first = Score(content, WithNonSubstantive("0.3"), answers);
        var second = Score(content, WithNonSubstantive("0.3"), answers);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void A_total_with_no_substantive_answer_is_incomplete_without_a_total()
    {
        var content = Module(3, nonSubstantive: true);

        var computation = Score(content, WithNonSubstantive("1"), AnswerAll(content, 99, 99, 99));

        Assert.Equal(ScoreStatus.Incomplete, computation.Status);
        Assert.Equal(ScoreIncompleteReasons.NoSubstantiveAnswers, computation.IncompleteReason);
        Assert.Null(computation.TotalScore);
    }

    [Fact]
    public void A_category_with_no_substantive_answer_makes_the_result_incomplete_even_when_the_total_is_valid()
    {
        var content = Content(
            Item("Q01", category: "debt", nonSubstantive: true),
            Item("Q02", category: "finance"), Item("Q03", category: "finance"), Item("Q04", category: "finance"),
            Item("Q05", category: "finance"), Item("Q06", category: "finance"), Item("Q07", category: "finance"),
            Item("Q08", category: "finance"), Item("Q09", category: "finance"), Item("Q10", category: "finance"));

        var computation = Score(content, WithNonSubstantive("0.3"), AnswerAll(content, 99, 3, 3, 3, 3, 3, 3, 3, 3, 3));

        Assert.Equal(ScoreStatus.Incomplete, computation.Status);
        Assert.Equal(ScoreIncompleteReasons.NoSubstantiveAnswers, computation.IncompleteReason);
        Assert.Null(computation.TotalScore);
        Assert.Empty(computation.CategoryScores);
    }
}
