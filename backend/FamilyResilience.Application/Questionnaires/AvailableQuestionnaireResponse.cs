using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>A questionnaire whose approved version is in effect now, with its sections (modules) by code and title.</summary>
public sealed record AvailableQuestionnaireResponse(
    string InstrumentCode,
    InstrumentKind Kind,
    TextDto Name,
    int VersionNumber,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<SectionSummaryResponse> Sections);
