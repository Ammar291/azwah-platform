using FamilyResilience.Domain.Questionnaires;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Questionnaires;

public sealed class RuleSetTargetTests
{
    [Fact]
    public void A_family_voices_rule_targets_one_module_of_the_version()
    {
        var version = DraftVersion();

        var section = Ok(RuleSetTarget.Resolve(InstrumentOf(version), version, "B"));

        Assert.Equal(version.Sections[1].Id, section?.Id);
    }

    [Fact]
    public void A_family_voices_rule_must_name_a_module()
    {
        var version = DraftVersion();

        Assert.Equal(QuestionnaireErrors.RuleSetTargetInvalid, RuleSetTarget.Resolve(InstrumentOf(version), version, null).Error);
    }

    [Fact]
    public void A_household_assessment_rule_covers_the_whole_version()
    {
        var instrument = NewInstrument("household_test", InstrumentKind.HouseholdAssessment);
        var version = DraftVersion(instrument);

        Assert.Null(Ok(RuleSetTarget.Resolve(instrument, version, null)));
        Assert.Equal(QuestionnaireErrors.RuleSetTargetInvalid, RuleSetTarget.Resolve(instrument, version, "A").Error);
    }

    [Fact]
    public void An_unknown_module_is_rejected()
    {
        var version = DraftVersion();

        Assert.Equal(QuestionnaireErrors.SectionNotFound, RuleSetTarget.Resolve(InstrumentOf(version), version, "Z").Error);
    }

    [Fact]
    public void The_version_must_belong_to_the_instrument() =>
        Assert.Equal(
            QuestionnaireErrors.InstrumentMismatch,
            RuleSetTarget.Resolve(NewInstrument(), DraftVersion(), "A").Error);
}
