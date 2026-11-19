using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed record InstrumentVersionResponse(
    Guid Id,
    Guid InstrumentId,
    int VersionNumber,
    InstrumentVersionStatus Status,
    string? ContentReferenceAr,
    string? ContentReferenceEn,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? ApprovalSource,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? RetiredAt,
    bool HasCompleteText,
    IReadOnlyList<SectionResponse> Sections);
