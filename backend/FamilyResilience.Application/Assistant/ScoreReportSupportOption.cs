namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A service the graph offers a report model to choose from, in the reader's language (D-115). Public facts only: the code it
/// must copy, the need that led here, the service, who offers it and where, what it is, and whether it is private.
/// </summary>
public sealed record ScoreReportSupportOption(
    string Code, string Need, string Service, string OfferedBy, string Kind, IReadOnlyList<string> Serves, string About)
{
    /// <summary>Which area this option belongs to, when a whole report is written at once.</summary>
    public string? SectionCode { get; init; }

    public static ScoreReportSupportOption From(SupportCandidate candidate, string language, string? sectionCode = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new ScoreReportSupportOption(
            candidate.Service.Code,
            AssistantLanguages.Pick(candidate.Need.Label, language),
            AssistantLanguages.Pick(candidate.Service.Name, language),
            AssistantLanguages.Pick(candidate.Authority.Name, language),
            candidate.Authority.Kind,
            candidate.Authority.Emirates,
            AssistantLanguages.Pick(candidate.Service.Summary, language))
        {
            SectionCode = sectionCode,
        };
    }
}
