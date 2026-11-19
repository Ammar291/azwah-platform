using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Infrastructure.Content;
using static FamilyResilience.UnitTests.Domain.DomainTestData;
using static FamilyResilience.UnitTests.Domain.Scoring.ScoringTestContent;

namespace FamilyResilience.UnitTests.SourceContent;

/// <summary>
/// The brief's proposed scoring for the household assessment (5 − raw on reverse items, a 39–156 total, three bands),
/// expressed as a committed <c>scoring.v1</c> definition that the platform's own strategy evaluates. The numbers here
/// are what the demo computes for the same answers. Nothing is approved (open question 2).
/// </summary>
public sealed class HouseholdAssessmentScoringTests
{
    private static readonly InstrumentContentPackage Package = ContentFiles.ReadPackage(ContentFiles.HouseholdAssessment);
    private static readonly string Definition = ContentFiles.Read(ContentFiles.HouseholdAssessmentScoring);
    private static readonly Instrument Instrument = Ok(InstrumentContentImporter.CreateInstrument(Package.Instrument, Now));
    private static readonly InstrumentVersion Draft = Ok(InstrumentContentImporter.BuildDraft(Instrument, 1, Package.Version, Now));
    private static readonly ScoringContent Content = Ok(ScoringContent.From(Draft, null));
    private static readonly SummedItemValuesStrategy Strategy = new();

    [Fact]
    public void The_proposed_definition_is_valid_for_the_draft_and_can_be_proposed_as_a_scoring_rule_set()
    {
        var validator = new ScoringDefinitionValidator(new ScoringStrategyCatalog([Strategy]));

        Assert.True(validator.Validate(Definition, Instrument, Draft).IsSuccess);
        Assert.Equal(("household_assessment_total", ContentFiles.HouseholdAssessmentScoring), Assert.Single(ContentFiles.ProposedScoringRules));
        var proposed = Ok(RuleSet.Propose(RuleSetKind.Scoring, "household_assessment_total", 1, Draft, Definition, Now));
        Assert.Equal(RuleSetStatus.Proposed, proposed.Status);
    }

    [Fact]
    public void Ten_statements_are_reverse_scored() =>
        Assert.Equal(
            new[] { "Q04", "Q07", "Q11", "Q16", "Q21", "Q25", "Q26", "Q32", "Q36", "Q38" },
            Content.Items.Where(item => item.Direction == ScoringDirection.Reverse).Select(item => item.Code));

    [Theory]
    [InlineData(3, 3, 107, "transitional")]
    [InlineData(1, 4, 39, "vulnerable")]
    [InlineData(4, 1, 156, "flourishing")]
    [InlineData(1, 1, 69, "vulnerable")]
    [InlineData(4, 4, 126, "flourishing")]
    public void Totals_and_tiers_follow_the_brief(int forwardRaw, int reverseRaw, int total, string tier)
    {
        var computation = Score(item => item.Direction == ScoringDirection.Reverse ? reverseRaw : forwardRaw);

        Assert.Equal(ScoreStatus.Complete, computation.Status);
        Assert.Equal(total, computation.TotalScore);
        Assert.Equal(tier, computation.TierCode);
        Assert.Equal(SummedItemValuesStrategy.Code, computation.PolicyCode);
        Assert.Equal(1, computation.AlgorithmVersion);
    }

    [Theory]
    [InlineData(2, 3, 1, 78, "vulnerable")]
    [InlineData(2, 3, 2, 79, "transitional")]
    [InlineData(3, 2, 1, 117, "transitional")]
    [InlineData(3, 2, 2, 118, "flourishing")]
    public void Band_edges_belong_to_the_band_that_names_them(int forwardRaw, int reverseRaw, int q01Shift, int total, string tier)
    {
        var computation = Score(item => item.Code == "Q01" ? forwardRaw + q01Shift - 1 : item.Direction == ScoringDirection.Reverse ? reverseRaw : forwardRaw);

        Assert.Equal(total, computation.TotalScore);
        Assert.Equal(tier, computation.TierCode);
    }

    [Fact]
    public void A_reverse_scored_statement_answered_strongly_disagree_counts_four()
    {
        var computation = Score(item => 1);

        var q04 = computation.Contributions.Single(contribution => contribution.ItemId == Content.Items.Single(item => item.Code == "Q04").ItemId);
        Assert.Equal(1, q04.RawValue);
        Assert.Equal(4m, q04.CalculatedValue);
    }

    [Fact]
    public void Category_scores_sum_each_of_the_seven_areas()
    {
        var computation = Score(item => 3);

        Assert.Equal(
            new[]
            {
                ("communication", 19m, 7), ("connectedness", 14m, 5), ("extended_family", 14m, 5), ("outlook", 14m, 5),
                ("seniors", 14m, 5), ("wealth_debt", 16m, 6), ("work_life", 16m, 6),
            },
            computation.CategoryScores.Select(category => (category.CategoryCode, category.Score, category.ItemCount)));
    }

    private static ScoringComputation Score(Func<ScoringItem, int> rawFor)
    {
        var parsed = Ok(ScoringDefinitionParser.Parse(Definition));
        var parameters = Ok(Strategy.ValidateParameters(parsed.Parameters, Content));
        var responses = Content.Items.Select(item => Answer(item, rawFor(item))).ToArray();
        return Ok(Strategy.Score(new ScoringInput(Content, parameters, responses)));
    }
}
