using FamilyResilience.Domain.Questionnaires;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Questionnaires;

public sealed class InstrumentVersionDraftCopyTests
{
    [Fact]
    public void A_new_draft_copies_every_section_item_and_option_with_new_ids()
    {
        var instrument = NewInstrument();
        var source = ApprovedVersion(instrument);

        var copy = Ok(InstrumentVersion.CreateDraftFrom(instrument, source, 2, Now));

        Assert.Equal(InstrumentVersionStatus.Draft, copy.Status);
        Assert.Equal(2, copy.VersionNumber);
        Assert.Equal(instrument.Id, copy.InstrumentId);
        Assert.Equal(source.ContentReferenceAr, copy.ContentReferenceAr);
        Assert.Equal(source.ContentReferenceEn, copy.ContentReferenceEn);
        Assert.Equal(
            source.Sections.Select(section => (section.Code, section.DisplayOrder, section.Title)),
            copy.Sections.Select(section => (section.Code, section.DisplayOrder, section.Title)));

        var sourceItems = source.GetItems().ToList();
        var copiedItems = copy.GetItems().ToList();
        Assert.Equal(
            sourceItems.Select(item => (item.Code, item.DisplayOrder, item.IsRequired, item.ScoringCategory, item.ScoringDirection, item.Prompt)),
            copiedItems.Select(item => (item.Code, item.DisplayOrder, item.IsRequired, item.ScoringCategory, item.ScoringDirection, item.Prompt)));
        Assert.Equal(
            sourceItems.SelectMany(item => item.Options).Select(option => (option.Code, option.Value, option.IsNonSubstantive, option.Label)),
            copiedItems.SelectMany(item => item.Options).Select(option => (option.Code, option.Value, option.IsNonSubstantive, option.Label)));
        Assert.DoesNotContain(copiedItems, item => sourceItems.Exists(original => original.Id == item.Id));
        Assert.All(copiedItems, item => Assert.Equal(copy.Id, item.InstrumentVersionId));
    }

    [Fact]
    public void The_copy_can_change_while_the_source_cannot()
    {
        var instrument = NewInstrument();
        var source = ApprovedVersion(instrument);
        var copy = Ok(InstrumentVersion.CreateDraftFrom(instrument, source, 2, Now));

        Assert.True(copy.AddSection("C", Text("قسم", "Section"), 3, Now).IsSuccess);
        Assert.Equal(QuestionnaireErrors.VersionNotDraft, source.AddSection("C", Text("قسم", "Section"), 3, Now).Error);
    }

    [Fact]
    public void A_copy_must_stay_within_its_instrument() =>
        Assert.Equal(
            QuestionnaireErrors.InstrumentMismatch,
            InstrumentVersion.CreateDraftFrom(NewInstrument(), ApprovedVersion(), 2, Now).Error);
}
