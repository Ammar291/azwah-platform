using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application;

public sealed class InstrumentContentImporterTests
{
    private static ContentOptionDto Option(string code, int value, bool nonSubstantive = false, string? arabic = null) =>
        new(code, value == 99 ? 9 : value, value, nonSubstantive, new TextDto(arabic, $"Label {code}"));

    private static InstrumentContentDocument Document(string sectionCode = "A", params ContentOptionDto[] options) =>
        new(
            "source (ar)",
            "source (en)",
            [
                new ContentSectionDto(sectionCode, 1, new TextDto(null, "Module A"),
                [
                    new ContentItemDto(
                        "Q04", 1, ItemType.SingleChoice, true, null, ScoringDirection.Unspecified, new TextDto("سؤال", "Question"),
                        options.Length > 0 ? options : [Option("1", 1), Option("2", 2), Option("99", 99, nonSubstantive: true)]),
                ]),
            ]);

    [Fact]
    public void A_document_becomes_a_draft_with_its_sections_items_and_options()
    {
        var instrument = NewInstrument();

        var draft = Ok(InstrumentContentImporter.BuildDraft(instrument, 3, Document(), Now));

        Assert.Equal(InstrumentVersionStatus.Draft, draft.Status);
        Assert.Equal(3, draft.VersionNumber);
        Assert.Equal("source (en)", draft.ContentReferenceEn);
        var section = Assert.Single(draft.Sections);
        Assert.Null(section.Title.Arabic);
        var item = Assert.Single(section.Items);
        Assert.Equal(ScoringDirection.Unspecified, item.ScoringDirection);
        Assert.True(item.IsRequired);
        Assert.Equal(new[] { 1, 2, 99 }, item.Options.Select(option => option.Value));
        Assert.True(item.Options[2].IsNonSubstantive);
    }

    [Fact]
    public void Blank_arabic_counts_as_missing() =>
        Assert.Null(Ok(InstrumentContentImporter.BuildDraft(
            NewInstrument(), 1, Document("A", Option("1", 1, arabic: "  "), Option("2", 2)), Now)).GetItems().Single().Options[0].Label.Arabic);

    [Fact]
    public void An_invalid_code_is_refused_with_where_it_is()
    {
        var built = InstrumentContentImporter.BuildDraft(NewInstrument(), 1, Document("A A"), Now);

        Assert.Equal(QuestionnaireErrors.CodeInvalid.Code, built.Error?.Code);
        Assert.Contains("(section A A)", built.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_non_substantive_option_is_refused_with_where_it_is()
    {
        var built = InstrumentContentImporter.BuildDraft(
            NewInstrument(), 1, Document("A", Option("1", 1), Option("98", 98, nonSubstantive: true), Option("99", 99, nonSubstantive: true)), Now);

        Assert.Equal(QuestionnaireErrors.NonSubstantiveOptionTaken.Code, built.Error?.Code);
        Assert.Contains("option 99 of item Q04", built.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_instrument_may_be_created_without_arabic_names()
    {
        var instrument = Ok(InstrumentContentImporter.CreateInstrument(
            new CreateInstrumentRequest("draft_survey", InstrumentKind.FamilyVoices, new TextDto(null, "Survey"), new TextDto(null, "About")),
            Now));

        Assert.False(instrument.Name.IsComplete);
        Assert.Equal("Survey", instrument.Name.English);
    }
}
