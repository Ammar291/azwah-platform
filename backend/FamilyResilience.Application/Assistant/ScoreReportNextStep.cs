namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One step of a family's flow (D-114, D-115): where to improve, why and what to start at home, in a model's words, then where
/// to go and the procedure there, from the knowledge graph. <see cref="Standing"/> runs from 0 (the area's lowest possible) to
/// 1 (its highest); the app orders steps by it, lowest first, and never shows it.
/// </summary>
public sealed record ScoreReportNextStep(
    string SectionCode,
    string Area,
    string Reason,
    IReadOnlyList<string> Steps,
    decimal Standing,
    IReadOnlyList<ScoreReportAuthority> Authorities)
{
    /// <summary>
    /// A family's emirate is not known, so each emirate's families get this many at most from what a model picked: one, so a
    /// step stays short (D-145; two before).
    /// </summary>
    public const int MaxServicesPerCoverage = 1;

    /// <summary>Where a score stands between the area's lowest and highest. An area with no range sorts last.</summary>
    public static decimal StandingOf(decimal score, int minimum, int maximum) =>
        maximum <= minimum ? 1m : decimal.Round(Math.Clamp((score - minimum) / (maximum - minimum), 0m, 1m), 3);

    /// <summary>
    /// A model's improvement joined with the graph. Inference offers the candidates; a model's pick narrows them and can add
    /// nothing, because a code outside the candidates is dropped. Text only ever comes from the graph.
    /// </summary>
    public static ScoreReportNextStep From(
        ScoreReportImprovement improvement,
        string sectionCode,
        decimal standing,
        string language,
        IReadOnlyList<SupportCandidate> candidates,
        ISupportKnowledgeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(improvement);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(graph);
        var chosen = Choose(candidates, improvement.SupportCodes ?? []);
        var authorities = chosen
            .GroupBy(candidate => candidate.Authority.Code, StringComparer.Ordinal)
            .Select(group =>
            {
                var authority = group.First().Authority;
                return new ScoreReportAuthority(
                    authority.Code,
                    authority.Kind,
                    AssistantLanguages.Pick(authority.Name, language),
                    AssistantLanguages.Pick(authority.Purpose, language),
                    authority.Emirates,
                    group.Select(candidate => Service(candidate, language, graph)).ToList(),
                    authority.Website,
                    authority.Phone,
                    authority.CheckedOn);
            })
            .ToList();
        return new ScoreReportNextStep(sectionCode, improvement.Area, improvement.Reason, improvement.Steps, standing, authorities);
    }

    /// <summary>
    /// What a family is shown. The model's valid picks come first, at most <see cref="MaxServicesPerCoverage"/> for each area of
    /// coverage. With no valid pick the graph's order decides among public services, so a fee-charging provider appears only
    /// because a model picked it or nothing else was reached. Because a family's emirate is not known, a public service
    /// covering an emirate the picks left out is added too, so a family there still has somewhere to go.
    /// </summary>
    public static IReadOnlyList<SupportCandidate> Choose(IReadOnlyList<SupportCandidate> candidates, IReadOnlyList<string> picks)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(picks);
        var valid = picks
            .Distinct(StringComparer.Ordinal)
            .Select(code => candidates.FirstOrDefault(candidate => candidate.Service.Code == code))
            .OfType<SupportCandidate>()
            .ToList();
        var publicOnes = candidates.Where(candidate => candidate.Authority.Kind != SupportKnowledgeGraph.PrivateKind).ToList();
        IReadOnlyList<SupportCandidate> ordered = valid.Count > 0 ? valid : publicOnes.Count > 0 ? publicOnes : candidates;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var chosen = new List<SupportCandidate>();
        foreach (var candidate in ordered)
        {
            var coverage = Coverage(candidate.Authority);
            if (counts.GetValueOrDefault(coverage) < MaxServicesPerCoverage)
            {
                counts[coverage] = counts.GetValueOrDefault(coverage) + 1;
                chosen.Add(candidate);
            }
        }

        foreach (var candidate in publicOnes)
        {
            if (counts.TryAdd(Coverage(candidate.Authority), 1))
            {
                chosen.Add(candidate);
            }
        }

        return chosen;
    }

    private static string Coverage(SupportAuthority authority) => string.Join(',', authority.Emirates);

    private static ScoreReportService Service(SupportCandidate candidate, string language, ISupportKnowledgeGraph graph)
    {
        var service = candidate.Service;
        var via = service.Via is { } code && code != candidate.Authority.Code ? graph.FindAuthority(code) : null;
        return new ScoreReportService(
            service.Code,
            AssistantLanguages.Pick(service.Name, language),
            AssistantLanguages.Pick(service.Summary, language),
            AssistantLanguages.Pick(candidate.Need.Label, language),
            service.Procedure.Select(step => AssistantLanguages.Pick(step, language)).ToList(),
            service.Documents.Select(document => AssistantLanguages.Pick(document, language)).ToList(),
            service.Website,
            via is null ? null : AssistantLanguages.Pick(via.Name, language));
    }
}
