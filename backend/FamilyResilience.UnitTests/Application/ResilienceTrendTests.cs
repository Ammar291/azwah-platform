using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.UnitTests.Application;

/// <summary>
/// Which check-ins the family's trend chart shows (D-132): recorded totals only, oldest first, on the scale of the latest
/// one; points join only while the same rule set scored them. Synthetic scales and totals, no real scoring rule.
/// </summary>
public sealed class ResilienceTrendTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly ScoreScale Scale = new(10, 40, [new TierBand("band_low", 10, 25), new TierBand("band_high", 26, 40)]);
    private static readonly RuleSetId First = RuleSetId.From(Guid.CreateVersion7());
    private static readonly RuleSetId Second = RuleSetId.From(Guid.CreateVersion7());

    private static ResilienceCheckIn Scored(int month, decimal total, RuleSetId ruleSet, string tier = "band_low") =>
        new(Guid.CreateVersion7(), $"wave_{month}", Start.AddMonths(month), ScoreStatus.Complete, total, tier, ruleSet);

    private static ResilienceCheckIn Unscored(int month, ScoreStatus? status = null) =>
        new(Guid.CreateVersion7(), $"wave_{month}", Start.AddMonths(month), status, null, null, status is null ? null : First);

    private static Dictionary<RuleSetId, ScoreScale> Scales(params (RuleSetId Id, ScoreScale Scale)[] scales) =>
        scales.ToDictionary(pair => pair.Id, pair => pair.Scale);

    [Fact]
    public void Recorded_totals_are_listed_oldest_first_with_the_scale_and_bands_of_their_rule_set()
    {
        var later = Scored(6, 31, First, "band_high");
        var earlier = Scored(0, 18, First);

        var trend = ResilienceTrend.Build([later, earlier], Scales((First, Scale)));

        Assert.Equal(ScoreVisibility.Visible, trend.ScoreVisibility);
        Assert.Equal(
            [(earlier.SessionId, "wave_0", earlier.SubmittedAt, 18m, "band_low", 0), (later.SessionId, "wave_6", later.SubmittedAt, 31m, "band_high", 0)],
            trend.Points.Select(point => (point.SessionId, point.WaveCode, point.SubmittedAt, point.TotalScore, point.TierCode, point.Segment)));
        Assert.NotNull(trend.Scale);
        Assert.Equal((10, 40), (trend.Scale.Minimum, trend.Scale.Maximum));
        Assert.Equal([("band_low", 10, 25), ("band_high", 26, 40)], trend.Scale.Bands.Select(band => (band.Code, band.Min, band.Max)));
        Assert.Equal(0, trend.NotShownCount);
    }

    [Fact]
    public void Check_ins_without_a_recorded_total_are_counted_but_not_drawn()
    {
        var trend = ResilienceTrend.Build(
            [Scored(0, 18, First), Unscored(6), Unscored(12, ScoreStatus.Incomplete)], Scales((First, Scale)));

        Assert.Equal([18m], trend.Points.Select(point => point.TotalScore));
        Assert.Equal(2, trend.NotShownCount);
    }

    [Fact]
    public void Without_any_recorded_total_there_is_no_scale_and_no_point()
    {
        var trend = ResilienceTrend.Build([Unscored(0), Unscored(6, ScoreStatus.Incomplete)], Scales((First, Scale)));

        Assert.Equal((null, 0, 2), (trend.Scale, trend.Points.Count, trend.NotShownCount));
        Assert.Equal(ScoreVisibility.Visible, trend.ScoreVisibility);
    }

    [Fact]
    public void A_new_rule_set_on_the_same_scale_starts_a_new_segment_so_the_line_does_not_join_them()
    {
        var trend = ResilienceTrend.Build(
            [Scored(0, 18, First), Scored(6, 20, First), Scored(12, 22, Second), Scored(18, 24, Second)],
            Scales((First, Scale), (Second, Scale with { })));

        Assert.Equal([0, 0, 1, 1], trend.Points.Select(point => point.Segment));
        Assert.Equal(0, trend.NotShownCount);
    }

    [Fact]
    public void Totals_on_another_scale_than_the_latest_are_left_out_and_counted()
    {
        var older = new ScoreScale(5, 20, []);

        var trend = ResilienceTrend.Build([Scored(0, 12, Second), Scored(6, 30, First)], Scales((First, Scale), (Second, older)));

        Assert.Equal([30m], trend.Points.Select(point => point.TotalScore));
        Assert.Equal((10, 40, 1), (trend.Scale!.Minimum, trend.Scale.Maximum, trend.NotShownCount));
    }

    [Fact]
    public void A_total_whose_rule_set_scale_cannot_be_read_is_left_out_and_counted()
    {
        var trend = ResilienceTrend.Build([Scored(0, 18, First), Scored(6, 20, Second)], Scales((First, Scale)));

        Assert.Equal([18m], trend.Points.Select(point => point.TotalScore));
        Assert.Equal(1, trend.NotShownCount);
    }

    [Fact]
    public void Check_ins_submitted_at_the_same_moment_keep_one_order()
    {
        var a = Scored(0, 18, First);
        var b = a with { SessionId = Guid.CreateVersion7(), TotalScore = 21 };

        var forwards = ResilienceTrend.Build([a, b], Scales((First, Scale)));
        var backwards = ResilienceTrend.Build([b, a], Scales((First, Scale)));

        Assert.Equal(forwards.Points.Select(point => point.SessionId), backwards.Points.Select(point => point.SessionId));
    }

    [Fact]
    public void The_head_only_response_carries_no_scale_point_or_count()
    {
        var trend = ResilienceTrend.HeadOfHouseholdOnly;

        Assert.Equal((ScoreVisibility.HeadOfHouseholdOnly, null, 0, 0), (trend.ScoreVisibility, trend.Scale, trend.Points.Count, trend.NotShownCount));
    }
}
