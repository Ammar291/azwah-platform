using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Infrastructure.Content;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application;

public sealed class RuleDefinitionValidatorTests
{
    private static readonly EligibilityDefinitionValidator Eligibility = new();
    private static readonly ScoringDefinitionValidator Scoring = new(new ScoringStrategyCatalog([new SummedItemValuesStrategy()]));

    private static string ScoringFor(string section, string parameters = """{"reverseScoring":"mirror_scale","unanswered":"reject","total":true}""", string policy = "summed_item_values") =>
        $$"""{"schema":"scoring.v1","target":{"sectionCode":"{{section}}"},"policy":"{{policy}}","algorithmVersion":1,"parameters":{{parameters}}}""";

    [Fact]
    public void An_eligibility_rule_for_a_module_of_a_family_voices_version_is_valid()
    {
        var version = DraftVersion();

        Assert.True(Eligibility.Validate(
            """{"schema":"eligibility.v1","target":{"sectionCode":"A"},"rule":{"ageAtLeast":60}}""", InstrumentOf(version), version).IsSuccess);
    }

    [Fact]
    public void An_eligibility_rule_must_name_an_existing_module_of_a_family_voices_version()
    {
        var version = DraftVersion();

        Assert.Equal(
            QuestionnaireErrors.RuleSetTargetInvalid,
            Eligibility.Validate("""{"schema":"eligibility.v1","rule":{"ageAtLeast":60}}""", InstrumentOf(version), version).Error);
        Assert.Equal(
            QuestionnaireErrors.SectionNotFound,
            Eligibility.Validate("""{"schema":"eligibility.v1","target":{"sectionCode":"Z"},"rule":{"ageAtLeast":60}}""", InstrumentOf(version), version).Error);
    }

    [Fact]
    public void A_household_assessment_rule_covers_the_whole_version()
    {
        var instrument = NewInstrument("household_rules", InstrumentKind.HouseholdAssessment);
        var version = DraftVersion(instrument);

        Assert.True(Eligibility.Validate("""{"schema":"eligibility.v1","rule":{"ageAtLeast":18}}""", instrument, version).IsSuccess);
        Assert.Equal(
            QuestionnaireErrors.RuleSetTargetInvalid,
            Eligibility.Validate("""{"schema":"eligibility.v1","target":{"sectionCode":"A"},"rule":{"ageAtLeast":18}}""", instrument, version).Error);
    }

    [Fact]
    public void An_invalid_eligibility_definition_is_refused()
    {
        var version = DraftVersion();

        Assert.Equal(EligibilityErrors.DefinitionInvalid.Code, Eligibility.Validate("{}", InstrumentOf(version), version).Error?.Code);
    }

    [Fact]
    public void A_scoring_rule_for_a_module_with_a_known_policy_is_valid()
    {
        var version = DraftVersion();

        Assert.True(Scoring.Validate(ScoringFor("A"), InstrumentOf(version), version).IsSuccess);
    }

    [Fact]
    public void A_scoring_rule_naming_an_unknown_policy_is_unsupported()
    {
        var version = DraftVersion();

        Assert.Equal(ScoringErrors.PolicyUnsupported, Scoring.Validate(ScoringFor("A", policy: "weighted_sum"), InstrumentOf(version), version).Error);
    }

    [Fact]
    public void A_scoring_rule_whose_bands_do_not_cover_the_module_is_refused()
    {
        // Section A of the test version has two items valued 1–3, so totals run 2–6.
        var version = DraftVersion();
        var parameters = """{"reverseScoring":"mirror_scale","unanswered":"reject","total":true,"tierBands":[{"code":"band_all","min":2,"max":5}]}""";

        Assert.Equal(ScoringErrors.TierBandsInvalid.Code, Scoring.Validate(ScoringFor("A", parameters), InstrumentOf(version), version).Error?.Code);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public void The_codebook_modules_cannot_be_scored_until_item_directions_are_approved(string module)
    {
        var package = ContentFiles.ReadPackage(ContentFiles.AbuDhabiFamilySurvey);
        var instrument = Ok(InstrumentContentImporter.CreateInstrument(package.Instrument, Now));
        var draft = Ok(InstrumentContentImporter.BuildDraft(instrument, 1, package.Version, Now));
        var parameters = """{"reverseScoring":"mirror_scale","unanswered":"reject","total":true,"nonSubstantive":{"maxShare":0.3,"aggregation":"prorated_mean","rounding":{"mode":"half_away_from_zero","decimals":0}}}""";

        Assert.Equal(ScoringErrors.ItemDirectionUnspecified.Code, Scoring.Validate(ScoringFor(module, parameters), instrument, draft).Error?.Code);
    }
}
