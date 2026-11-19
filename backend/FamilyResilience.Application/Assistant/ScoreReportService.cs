namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A service in a report's next steps, word for word from the knowledge graph (D-115). An empty <see cref="Procedure"/> means
/// no source publishes one. <see cref="Via"/> names the authority it is applied for through, if another.
/// </summary>
public sealed record ScoreReportService(
    string Code,
    string Name,
    string Summary,
    string Need,
    IReadOnlyList<string> Procedure,
    IReadOnlyList<string> Documents,
    string? Website,
    string? Via);
