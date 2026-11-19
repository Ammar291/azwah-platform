using System.Text.Json;
using CsCheck;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using static FamilyResilience.UnitTests.Domain.DomainTestData;
using static FamilyResilience.UnitTests.Domain.Scoring.ScoringTestContent;

namespace FamilyResilience.UnitTests.Domain.Scoring;

public sealed class ScoringPropertyTests
{
    private static readonly SummedItemValuesStrategy Strategy = new();

    /// <summary>1–12 items on one 1..n scale (n from 2 to 7), each forward or reverse, each answered on the scale.</summary>
    private static readonly Gen<(ScoringContent Content, ScoringResponse[] Responses)> AnsweredScopes =
        from scaleSize in Gen.Int[2, 7]
        from reversed in Gen.Bool.Array[1, 12]
        from seeds in Gen.Int[0, 1000].Array[12, 12]
        select Build(scaleSize, reversed, seeds);

    private static (ScoringContent Content, ScoringResponse[] Responses) Build(int scaleSize, bool[] reversed, int[] seeds)
    {
        var items = reversed
            .Select((isReverse, index) => Item($"Q{index:00}", isReverse ? ScoringDirection.Reverse : ScoringDirection.Forward, scaleSize))
            .ToList();
        var responses = items.Select((item, index) => Answer(item, 1 + (seeds[index] % scaleSize))).ToArray();
        return (new ScoringContent(items), responses);
    }

    private static ScoringComputation ScoreOf(ScoringContent content, ScoringResponse[] responses, string parameters = Minimal) =>
        Ok(Strategy.Score(new ScoringInput(content, Ok(Strategy.ValidateParameters(Json(parameters), content)), responses)));

    [Fact]
    public void Scoring_is_deterministic() =>
        AnsweredScopes.Sample(scope =>
            Assert.Equal(
                JsonSerializer.Serialize(ScoreOf(scope.Content, scope.Responses)),
                JsonSerializer.Serialize(ScoreOf(scope.Content, scope.Responses))));

    [Fact]
    public void A_complete_total_stays_in_range_and_equals_the_sum_of_the_contributions() =>
        AnsweredScopes.Sample(scope =>
        {
            var computation = ScoreOf(scope.Content, scope.Responses);

            Assert.InRange(computation.TotalScore ?? -1, scope.Content.MinimumTotal(), scope.Content.MaximumTotal());
            Assert.Equal(computation.Contributions.Sum(contribution => contribution.CalculatedValue ?? 0), computation.TotalScore);
        });

    [Fact]
    public void Mirroring_twice_returns_the_answer_and_never_leaves_the_scale() =>
        (from scaleSize in Gen.Int[2, 7]
         from raw in Gen.Int[1, scaleSize]
         select (scaleSize, raw))
        .Sample(sample =>
        {
            var item = Item("Q01", ScoringDirection.Reverse, sample.scaleSize);
            var content = new ScoringContent([item]);

            var once = (int)(ScoreOf(content, [Answer(item, sample.raw)]).TotalScore ?? 0);
            var twice = (int)(ScoreOf(content, [Answer(item, once)]).TotalScore ?? 0);

            Assert.InRange(once, 1, sample.scaleSize);
            Assert.Equal(sample.raw, twice);
        });

    [Fact]
    public void Valid_bands_always_place_a_total_in_exactly_one_tier() =>
        (from itemCount in Gen.Int[1, 13]
         from scaleSize in Gen.Int[2, 5]
         from cuts in Gen.Int[0, 1000].Array[2, 2]
         from seeds in Gen.Int[0, 1000].Array[13, 13]
         select (itemCount, scaleSize, cuts, seeds))
        .Sample(sample =>
        {
            var content = Module(sample.itemCount, sample.scaleSize);
            var (minimum, maximum) = (content.MinimumTotal(), content.MaximumTotal());
            var bands = Partition(minimum, maximum, sample.cuts);
            var parameters = Minimal[..^1] + Bands([.. bands]) + "}";
            var responses = content.Items.Select((item, index) => Answer(item, 1 + (sample.seeds[index] % sample.scaleSize))).ToArray();

            var computation = ScoreOf(content, responses, parameters);

            var containing = bands.Where(band => band.Min <= computation.TotalScore && computation.TotalScore <= band.Max).ToList();
            Assert.Equal(Assert.Single(containing).Code, computation.TierCode);
        });

    /// <summary>One to three contiguous bands exactly covering <paramref name="minimum"/>..<paramref name="maximum"/>.</summary>
    private static List<(string Code, int Min, int Max)> Partition(int minimum, int maximum, int[] cuts)
    {
        if (maximum - minimum < 2)
        {
            return [("band_all", minimum, maximum)];
        }

        var first = minimum + (cuts[0] % (maximum - minimum - 1));
        var second = first + 1 + (cuts[1] % (maximum - 1 - first));
        return [("band_low", minimum, first), ("band_mid", first + 1, second), ("band_high", second + 1, maximum)];
    }
}
