using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.Application.Assistant;

/// <summary>A service on the All services page, in both languages. An empty procedure means no source publishes one.</summary>
public sealed record SupportServiceResponse(
    string Code,
    TextDto Name,
    TextDto Summary,
    IReadOnlyList<TextDto> Procedure,
    IReadOnlyList<TextDto> Documents,
    string? Website,
    TextDto? Via);
