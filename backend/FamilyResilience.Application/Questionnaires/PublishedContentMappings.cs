using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>Maps approved content to what participants see. Scoring metadata and option values never leave the server.</summary>
internal static class PublishedContentMappings
{
    public static bool IsPublished(this InstrumentVersion version) =>
        version.Status is InstrumentVersionStatus.Approved or InstrumentVersionStatus.Retired;

    public static PublishedVersionResponse ToPublished(this InstrumentVersion version, Instrument instrument) =>
        new(
            instrument.Code,
            instrument.Kind,
            instrument.Name.ToDto(),
            instrument.Description.ToDto(),
            version.Id.Value,
            version.VersionNumber,
            version.Status,
            version.EffectiveFrom,
            version.EffectiveTo,
            version.Sections.OrderBy(section => section.DisplayOrder).Select(section => new PublishedSectionResponse(
                section.Id.Value,
                section.Code,
                section.DisplayOrder,
                section.Title.ToDto(),
                section.Items.OrderBy(item => item.DisplayOrder).Select(item => new PublishedItemResponse(
                    item.Id.Value,
                    item.Code,
                    item.DisplayOrder,
                    item.ItemType,
                    item.IsRequired,
                    item.Prompt.ToDto(),
                    item.Options.OrderBy(option => option.DisplayOrder)
                        .Select(option => new PublishedOptionResponse(
                            option.Id.Value, option.Code, option.DisplayOrder, option.IsNonSubstantive, option.Label.ToDto()))
                        .ToList()))
                    .ToList()))
                .ToList());

    public static AvailableQuestionnaireResponse ToAvailable(this InstrumentVersion version, Instrument instrument) =>
        new(
            instrument.Code,
            instrument.Kind,
            instrument.Name.ToDto(),
            version.VersionNumber,
            version.EffectiveFrom,
            version.EffectiveTo,
            version.Sections.OrderBy(section => section.DisplayOrder)
                .Select(section => new SectionSummaryResponse(section.Code, section.Title.ToDto()))
                .ToList());
}
