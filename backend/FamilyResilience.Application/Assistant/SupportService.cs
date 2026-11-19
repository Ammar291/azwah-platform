using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One thing an authority offers, a node of the support knowledge graph (D-115). <see cref="Needs"/> are its edges to the
/// needs it addresses and <see cref="Via"/> an edge to the authority it is applied for through. An empty
/// <see cref="Procedure"/> means no source publishes one, which the app says plainly. <see cref="Evidence"/> records how
/// its facts were seen: <c>opened</c> (the page itself was read) or <c>search_extract</c> (a search summary of the page).
/// </summary>
public sealed record SupportService(
    string Code,
    TextDto Name,
    TextDto Summary,
    IReadOnlyList<string> Needs,
    IReadOnlyList<TextDto> Procedure,
    IReadOnlyList<TextDto> Documents,
    string? Website,
    string? Via,
    string Evidence,
    IReadOnlyList<string> Sources);
