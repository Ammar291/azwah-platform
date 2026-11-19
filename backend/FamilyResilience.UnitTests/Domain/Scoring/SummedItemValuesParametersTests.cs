using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using static FamilyResilience.UnitTests.Domain.DomainTestData;
using static FamilyResilience.UnitTests.Domain.Scoring.ScoringTestContent;

namespace FamilyResilience.UnitTests.Domain.Scoring;

public sealed class SummedItemValuesParametersTests
{
    private const string WithoutTotal = """{"reverseScoring":"mirror_scale","unanswered":"reject","total":false}""";

    private static void AssertInvalidAt(string json, ScoringContent content, string path)
    {
        var read = SummedItemValuesParameters.Read(Json(json), content);

        Assert.Equal(ScoringErrors.DefinitionInvalid.Code, read.Error?.Code);
        Assert.Contains($" at {path}:", read.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Minimal_parameters_give_a_total_without_bands_or_exclusions()
    {
        var parameters = Ok(SummedItemValuesParameters.Read(Json(Minimal), Module(3)));

        Assert.True(parameters.Total);
        Assert.Null(parameters.NonSubstantive);
        Assert.Empty(parameters.TierBands);
    }

    [Theory]
    [InlineData("""{"unanswered":"reject","total":true}""", "$.parameters.reverseScoring")]
    [InlineData("""{"reverseScoring":"five_minus_raw","unanswered":"reject","total":true}""", "$.parameters.reverseScoring")]
    [InlineData("""{"reverseScoring":"mirror_scale","unanswered":"impute_mean","total":true}""", "$.parameters.unanswered")]
    [InlineData("""{"reverseScoring":"mirror_scale","unanswered":"reject"}""", "$.parameters.total")]
    [InlineData("""{"reverseScoring":"mirror_scale","unanswered":"reject","total":"yes"}""", "$.parameters.total")]
    [InlineData("""{"reverseScoring":"mirror_scale","unanswered":"reject","total":true,"weights":[1]}""", "$.parameters.weights")]
    [InlineData("[]", "$.parameters")]
    public void Every_choice_must_be_stated_explicitly(string json, string path) => AssertInvalidAt(json, Module(3), path);

    [Fact]
    public void An_optional_scored_item_is_refused_because_unanswered_items_are_rejected()
    {
        var read = SummedItemValuesParameters.Read(Json(Minimal), Content(Item("Q01"), Item("Q02") with { IsRequired = false }));

        Assert.Equal(ScoringErrors.OptionalItemScored.Code, read.Error?.Code);
        Assert.Contains("Q02", read.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_optional_item_that_is_not_scored_is_allowed() =>
        Ok(SummedItemValuesParameters.Read(Json(Minimal), Content(Item("Q01"), Item("Q02", ScoringDirection.NotScored) with { IsRequired = false })));

    [Fact]
    public void Without_a_total_a_policy_with_no_categorised_scored_item_is_refused() =>
        AssertInvalidAt(WithoutTotal, Content(Item("Q01"), Item("Q02"), Item("Q03", ScoringDirection.NotScored)), "$.parameters.total");

    [Fact]
    public void Without_a_total_one_categorised_scored_item_is_enough() =>
        Assert.False(Ok(SummedItemValuesParameters.Read(Json(WithoutTotal), Content(Item("Q01", category: "finance"), Item("Q02")))).Total);

    [Fact]
    public void An_item_without_an_approved_direction_blocks_the_policy()
    {
        var read = SummedItemValuesParameters.Read(
            Json(Minimal), Content(Item("Q04"), Item("Q05", ScoringDirection.Unspecified)));

        Assert.Equal(ScoringErrors.ItemDirectionUnspecified.Code, read.Error?.Code);
        Assert.Contains("Q05", read.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scope_without_a_scored_item_is_refused() =>
        Assert.Equal(
            ScoringErrors.NoScoredItems,
            SummedItemValuesParameters.Read(Json(Minimal), Content(Item("Q01", ScoringDirection.NotScored))).Error);

    [Fact]
    public void A_scored_item_needs_consecutive_values() =>
        Assert.Equal(
            ScoringErrors.ItemScaleInvalid.Code,
            SummedItemValuesParameters.Read(Json(Minimal), Content(ItemWithValues("Q01", [1, 2, 4]))).Error?.Code);

    [Fact]
    public void Items_that_are_not_scored_may_use_any_values() =>
        Assert.True(SummedItemValuesParameters.Read(
            Json(Minimal), Content(Item("Q01"), ItemWithValues("Q02", [1, 7], ScoringDirection.NotScored))).IsSuccess);

    [Fact]
    public void Items_offering_99_need_an_explicit_non_substantive_policy() =>
        AssertInvalidAt(Minimal, Module(10, nonSubstantive: true), "$.parameters.nonSubstantive");

    [Fact]
    public void A_non_substantive_policy_is_refused_when_no_item_offers_99() =>
        AssertInvalidAt(WithNonSubstantive(), Module(10), "$.parameters.nonSubstantive");

    [Fact]
    public void The_manuals_99_rule_can_be_stated_explicitly()
    {
        var parameters = Ok(SummedItemValuesParameters.Read(Json(WithNonSubstantive("0.3")), Module(10, nonSubstantive: true)));

        Assert.Equal(new NonSubstantivePolicy(0.3m, 0, MidpointRounding.AwayFromZero), parameters.NonSubstantive);
    }

    [Theory]
    [InlineData("1.5", "half_away_from_zero", 0, "$.parameters.nonSubstantive.maxShare")]
    [InlineData("-0.1", "half_away_from_zero", 0, "$.parameters.nonSubstantive.maxShare")]
    [InlineData("0.3", "up", 0, "$.parameters.nonSubstantive.rounding.mode")]
    [InlineData("0.3", "half_to_even", 4, "$.parameters.nonSubstantive.rounding.decimals")]
    public void Invalid_non_substantive_policies_are_refused(string maxShare, string mode, int decimals, string path) =>
        AssertInvalidAt(WithNonSubstantive(maxShare, mode, decimals), Module(10, nonSubstantive: true), path);

    [Fact]
    public void Aggregations_other_than_the_prorated_mean_are_refused() =>
        AssertInvalidAt(
            WithNonSubstantive().Replace("prorated_mean", "sum", StringComparison.Ordinal),
            Module(10, nonSubstantive: true),
            "$.parameters.nonSubstantive.aggregation");

    [Fact]
    public void Bands_that_tile_the_possible_totals_are_accepted_in_order()
    {
        var parameters = Ok(SummedItemValuesParameters.Read(
            Json(WithNonSubstantive(extra: Bands(("band_high", 41, 50), ("band_low", 10, 20), ("band_mid", 21, 40)))),
            Module(10, nonSubstantive: true)));

        Assert.Equal(new[] { "band_low", "band_mid", "band_high" }, parameters.TierBands.Select(band => band.Code));
    }

    [Theory]
    [InlineData(10, 20, 22, 50)]
    [InlineData(10, 25, 25, 50)]
    [InlineData(11, 30, 31, 50)]
    [InlineData(10, 30, 31, 51)]
    public void Bands_with_gaps_overlaps_or_the_wrong_range_are_refused(int lowMin, int lowMax, int highMin, int highMax) =>
        Assert.Equal(
            ScoringErrors.TierBandsInvalid.Code,
            SummedItemValuesParameters.Read(
                Json(WithNonSubstantive(extra: Bands(("band_low", lowMin, lowMax), ("band_high", highMin, highMax)))),
                Module(10, nonSubstantive: true)).Error?.Code);

    [Fact]
    public void The_household_assessment_bands_can_never_apply_to_a_module() =>
        Assert.Equal(
            ScoringErrors.TierBandsInvalid.Code,
            SummedItemValuesParameters.Read(
                Json(WithNonSubstantive(extra: Bands(("tier_1", 39, 78), ("tier_2", 79, 117), ("tier_3", 118, 156)))),
                Module(10, nonSubstantive: true)).Error?.Code);

    [Fact]
    public void Bands_need_a_total() =>
        Assert.Equal(
            ScoringErrors.TierBandsInvalid.Code,
            SummedItemValuesParameters.Read(
                Json(WithoutTotal[..^1] + Bands(("band_all", 3, 15)) + "}"),
                Content(Item("Q01", category: "finance"), Item("Q02", category: "finance"), Item("Q03", category: "finance"))).Error?.Code);

    [Fact]
    public void Bands_need_whole_number_totals() =>
        Assert.Equal(
            ScoringErrors.TierBandsInvalid.Code,
            SummedItemValuesParameters.Read(
                Json(WithNonSubstantive(decimals: 1, extra: Bands(("band_all", 10, 50)))), Module(10, nonSubstantive: true)).Error?.Code);

    [Fact]
    public void Band_codes_must_be_distinct_snake_codes()
    {
        Assert.Equal(
            ScoringErrors.TierBandsInvalid.Code,
            SummedItemValuesParameters.Read(Json(Minimal[..^1] + Bands(("band", 3, 9), ("band", 10, 15)) + "}"), Module(3)).Error?.Code);
        AssertInvalidAt(Minimal[..^1] + Bands(("Band 1", 3, 15)) + "}", Module(3), "$.parameters.tierBands[0].code");
    }

    [Fact]
    public void A_non_substantive_policy_is_refused_across_mixed_scales() =>
        AssertInvalidAt(
            WithNonSubstantive(),
            Content(Item("Q01", scaleSize: 4, nonSubstantive: true), Item("Q02", scaleSize: 5)),
            "$.parameters.nonSubstantive");

    [Fact]
    public void Mixed_scales_are_accepted_without_a_non_substantive_policy() =>
        Assert.True(SummedItemValuesParameters.Read(Json(Minimal), Content(Item("Q01", scaleSize: 4), Item("Q02", scaleSize: 5))).IsSuccess);
}
