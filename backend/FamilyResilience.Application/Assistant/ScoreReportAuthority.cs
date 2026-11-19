namespace FamilyResilience.Application.Assistant;

/// <summary>
/// An authority in a report's next steps, in the reader's language, word for word from the knowledge graph (D-115). A model
/// may pick among the services the graph reached but writes none of this. <see cref="CheckedOn"/> says how fresh it is.
/// </summary>
public sealed record ScoreReportAuthority(
    string Code,
    string Kind,
    string Name,
    string Purpose,
    IReadOnlyList<string> Emirates,
    IReadOnlyList<ScoreReportService> Services,
    string? Website,
    string? Phone,
    DateOnly CheckedOn);
