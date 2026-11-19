using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Questionnaires;

public sealed class InstrumentTests
{
    [Fact]
    public void Instrument_keeps_its_code_kind_and_bilingual_name()
    {
        var instrument = NewInstrument("household_assessment", InstrumentKind.HouseholdAssessment);

        Assert.Equal("household_assessment", instrument.Code);
        Assert.Equal(InstrumentKind.HouseholdAssessment, instrument.Kind);
        Assert.Equal("أداة", instrument.Name.Arabic);
        Assert.Equal(InstrumentStatus.Active, instrument.Status);
    }

    [Theory]
    [InlineData("Household")]
    [InlineData("household-assessment")]
    [InlineData("")]
    public void Code_must_be_a_snake_code(string code) =>
        Assert.Equal(
            QuestionnaireErrors.InstrumentCodeInvalid,
            Instrument.Create(code, InstrumentKind.FamilyVoices, Text("أداة", "Instrument"), Text("وصف", "Description"), Now).Error);

    [Fact]
    public void Name_longer_than_200_characters_is_rejected() =>
        Assert.Equal(
            QuestionnaireErrors.TextTooLong,
            Instrument.Create("code", InstrumentKind.FamilyVoices, Text("أداة", new string('n', 201)), Text("وصف", "Description"), Now).Error);

    [Fact]
    public void Undefined_kind_is_rejected() =>
        Assert.Equal(
            QuestionnaireErrors.InstrumentKindInvalid,
            Instrument.Create("code", (InstrumentKind)7, Text("أداة", "Instrument"), Text("وصف", "Description"), Now).Error);

    [Fact]
    public void Retired_instrument_cannot_get_new_versions()
    {
        var instrument = NewInstrument();
        Assert.True(instrument.Retire(Now).IsSuccess);

        Assert.Equal(QuestionnaireErrors.InstrumentAlreadyRetired, instrument.Retire(Now).Error);
        Assert.Equal(QuestionnaireErrors.InstrumentNotActive, InstrumentVersion.CreateDraft(instrument, 1, Now).Error);
    }

    [Fact]
    public void Instrument_text_can_be_completed_later()
    {
        var instrument = Ok(Instrument.Create(
            "draft_named", InstrumentKind.FamilyVoices, DraftText("Instrument"), DraftText("Description"), Now));
        var later = Now.AddHours(1);

        Assert.True(instrument.ChangeText(Text("أداة", "Instrument"), Text("وصف", "Description"), later).IsSuccess);

        Assert.True(instrument.Name.IsComplete);
        Assert.True(instrument.Description.IsComplete);
        Assert.Equal(later, instrument.UpdatedAt);
    }

    [Fact]
    public void Changed_instrument_text_must_be_bilingual() =>
        Assert.Equal(
            CommonErrors.TextRequired,
            NewInstrument().ChangeText(DraftText("Instrument"), Text("وصف", "Description"), Now).Error);
}
