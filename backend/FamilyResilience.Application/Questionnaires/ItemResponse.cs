using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed record ItemResponse(
    Guid Id,
    string Code,
    int DisplayOrder,
    ItemType ItemType,
    bool IsRequired,
    string? ScoringCategory,
    ScoringDirection ScoringDirection,
    TextDto Prompt,
    IReadOnlyList<OptionResponse> Options);
