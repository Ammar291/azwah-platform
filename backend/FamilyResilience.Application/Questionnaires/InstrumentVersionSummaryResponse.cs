using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>A version without its content: status, approval, effective window and how much content it holds.</summary>
public sealed record InstrumentVersionSummaryResponse(
    Guid Id,
    Guid InstrumentId,
    int VersionNumber,
    InstrumentVersionStatus Status,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? ApprovalSource,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? RetiredAt,
    int SectionCount,
    int ItemCount);
