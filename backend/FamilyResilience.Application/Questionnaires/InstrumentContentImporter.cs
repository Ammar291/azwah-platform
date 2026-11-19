using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>
/// Turns a content document into draft domain objects through the aggregates' own rules. Arabic may be missing (a
/// draft); every failure names where in the document it happened.
/// </summary>
public static class InstrumentContentImporter
{
    public static Result<Instrument> CreateInstrument(CreateInstrumentRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = Text(request.Name, Instrument.MaxNameLength, "instrument name");
        if (name.IsFailure)
        {
            return Result.Failure<Instrument>(name.Error!);
        }

        var description = Text(request.Description, Instrument.MaxDescriptionLength, "instrument description");
        return description.IsFailure
            ? Result.Failure<Instrument>(description.Error!)
            : Instrument.Create(request.Code, request.Kind, name.Value, description.Value, now);
    }

    public static Result<InstrumentVersion> BuildDraft(
        Instrument instrument, int versionNumber, InstrumentContentDocument document, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);

        var created = InstrumentVersion.CreateDraft(instrument, versionNumber, now);
        if (created.IsFailure)
        {
            return created;
        }

        var version = created.Value;
        var references = version.SetContentReferences(document.ContentReferenceAr, document.ContentReferenceEn, now);
        if (references.IsFailure)
        {
            return Result.Failure<InstrumentVersion>(references.Error!);
        }

        foreach (var sectionDto in document.Sections)
        {
            var sectionAt = $"section {sectionDto.Code}";
            var title = Text(sectionDto.Title, InstrumentSection.MaxTitleLength, sectionAt);
            if (title.IsFailure)
            {
                return Result.Failure<InstrumentVersion>(title.Error!);
            }

            var section = version.AddSection(sectionDto.Code, title.Value, sectionDto.DisplayOrder, now);
            if (section.IsFailure)
            {
                return Result.Failure<InstrumentVersion>(At(section.Error!, sectionAt));
            }

            foreach (var itemDto in sectionDto.Items)
            {
                var itemAt = $"item {itemDto.Code}";
                var prompt = Text(itemDto.Prompt, InstrumentItem.MaxPromptLength, itemAt);
                if (prompt.IsFailure)
                {
                    return Result.Failure<InstrumentVersion>(prompt.Error!);
                }

                var item = version.AddItem(
                    section.Value.Id, itemDto.Code, itemDto.DisplayOrder, itemDto.ItemType, itemDto.IsRequired,
                    itemDto.ScoringCategory, itemDto.ScoringDirection, prompt.Value, now);
                if (item.IsFailure)
                {
                    return Result.Failure<InstrumentVersion>(At(item.Error!, itemAt));
                }

                foreach (var optionDto in itemDto.Options)
                {
                    var optionAt = $"option {optionDto.Code} of item {itemDto.Code}";
                    var label = Text(optionDto.Label, ResponseOption.MaxLabelLength, optionAt);
                    if (label.IsFailure)
                    {
                        return Result.Failure<InstrumentVersion>(label.Error!);
                    }

                    var option = version.AddOption(
                        item.Value.Id, optionDto.Code, optionDto.DisplayOrder, optionDto.Value, label.Value, now, optionDto.IsNonSubstantive);
                    if (option.IsFailure)
                    {
                        return Result.Failure<InstrumentVersion>(At(option.Error!, optionAt));
                    }
                }
            }
        }

        return Result.Success(version);
    }

    private static Result<LocalizedText> Text(TextDto text, int maxLength, string at)
    {
        var created = LocalizedText.CreateDraft(text.Ar, text.En, maxLength);
        return created.IsFailure ? Result.Failure<LocalizedText>(At(created.Error!, at)) : created;
    }

    private static Error At(Error error, string at) => error with { Message = $"{error.Message} ({at})" };
}
