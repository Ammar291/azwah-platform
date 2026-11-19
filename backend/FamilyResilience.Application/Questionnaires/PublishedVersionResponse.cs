using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>
/// An approved (or since retired) questionnaire version as participants answer it, in display order. Drafts are never
/// published.
/// </summary>
public sealed record PublishedVersionResponse(
    string InstrumentCode,
    InstrumentKind Kind,
    TextDto Name,
    TextDto Description,
    Guid VersionId,
    int VersionNumber,
    InstrumentVersionStatus Status,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<PublishedSectionResponse> Sections);
