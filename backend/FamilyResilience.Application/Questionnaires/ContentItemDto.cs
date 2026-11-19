using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed record ContentItemDto(
    string Code,
    int DisplayOrder,
    ItemType ItemType,
    bool IsRequired,
    string? ScoringCategory,
    ScoringDirection ScoringDirection,
    TextDto Prompt,
    IReadOnlyList<ContentOptionDto> Options);
