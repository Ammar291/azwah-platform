using System.Text.Json;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>Maps questionnaire aggregates to response DTOs; domain objects never leave the Application layer.</summary>
internal static class QuestionnaireResponses
{
    public static TextDto ToDto(this LocalizedText text) => new(text.Arabic, text.English);

    public static InstrumentResponse ToResponse(this Instrument instrument) =>
        new(
            instrument.Id.Value,
            instrument.Code,
            instrument.Kind,
            instrument.Name.ToDto(),
            instrument.Description.ToDto(),
            instrument.Status,
            instrument.Name.IsComplete && instrument.Description.IsComplete);

    /// <summary>The version's response; its <c>HasCompleteText</c> also covers <paramref name="instrument"/>'s text, as approval does.</summary>
    public static InstrumentVersionResponse ToResponse(this InstrumentVersion version, Instrument instrument) =>
        new(
            version.Id.Value,
            version.InstrumentId.Value,
            version.VersionNumber,
            version.Status,
            version.ContentReferenceAr,
            version.ContentReferenceEn,
            version.ApprovedBy,
            version.ApprovedAt,
            version.ApprovalSource,
            version.EffectiveFrom,
            version.EffectiveTo,
            version.RetiredAt,
            version.HasCompleteText(instrument),
            version.Sections.OrderBy(section => section.DisplayOrder).Select(section => new SectionResponse(
                section.Id.Value,
                section.Code,
                section.DisplayOrder,
                section.Title.ToDto(),
                section.Items.OrderBy(item => item.DisplayOrder).Select(item => new ItemResponse(
                    item.Id.Value,
                    item.Code,
                    item.DisplayOrder,
                    item.ItemType,
                    item.IsRequired,
                    item.ScoringCategory,
                    item.ScoringDirection,
                    item.Prompt.ToDto(),
                    item.Options.OrderBy(option => option.DisplayOrder).Select(option => new OptionResponse(
                        option.Id.Value, option.Code, option.DisplayOrder, option.Value, option.IsNonSubstantive, option.Label.ToDto()))
                        .ToList()))
                    .ToList()))
                .ToList());

    public static RuleSetResponse ToResponse(this RuleSet ruleSet)
    {
        using var definition = JsonDocument.Parse(ruleSet.Definition);
        return new RuleSetResponse(
            ruleSet.Id.Value,
            ruleSet.Kind,
            ruleSet.Code,
            ruleSet.VersionNumber,
            ruleSet.InstrumentVersionId.Value,
            ruleSet.Status,
            definition.RootElement.Clone(),
            ruleSet.ApprovedBy,
            ruleSet.ApprovedAt,
            ruleSet.ApprovalSource,
            ruleSet.RetiredAt);
    }
}
