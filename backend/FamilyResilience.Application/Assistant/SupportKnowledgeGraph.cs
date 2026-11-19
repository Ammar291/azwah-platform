using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The graph read from its <c>support-knowledge-graph.v1</c> file, checked as a whole when the API starts. Any mistake stops
/// it, because a wrong procedure or number sends a family to the wrong place. Every text needs English and Arabic, every
/// edge must reach a real node, and every authority and service needs a source.
/// </summary>
public sealed partial class SupportKnowledgeGraph : ISupportKnowledgeGraph
{
    public const string Schema = "support-knowledge-graph.v1";

    public const string PrivateKind = "private";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "government", "public_provider", PrivateKind };

    private static readonly HashSet<string> Evidence = new(StringComparer.Ordinal) { "opened", "search_extract" };

    private static readonly HashSet<string> Coverage = new(StringComparer.Ordinal)
    {
        "uae", "abu_dhabi", "dubai", "sharjah", "ajman", "umm_al_quwain", "ras_al_khaimah", "fujairah",
    };

    private readonly Dictionary<string, SupportNeed> _needs;
    private readonly Dictionary<string, IReadOnlyList<string>> _signals;

    private SupportKnowledgeGraph(
        IReadOnlyList<SupportAuthority> authorities, Dictionary<string, SupportNeed> needs, Dictionary<string, IReadOnlyList<string>> signals)
    {
        Authorities = authorities;
        _needs = needs;
        _signals = signals;
    }

    public IReadOnlyList<SupportAuthority> Authorities { get; }

    /// <summary>The needs in the graph that no service addresses: what a family's step cannot yet point anywhere for.</summary>
    public IReadOnlyList<string> Gaps =>
        _needs.Keys.Where(need => !Authorities.Any(authority => authority.Services.Any(service => service.Needs.Contains(need)))).Order(StringComparer.Ordinal).ToList();

    /// <summary>The question codes with an edge to a need.</summary>
    public IReadOnlyCollection<string> Questions => _signals.Keys;

    public SupportAuthority? FindAuthority(string code) =>
        Authorities.FirstOrDefault(authority => authority.Code == code);

    public IReadOnlyList<SupportAuthority> Current(DateOnly today) =>
        Authorities.Where(authority => today <= authority.ReviewDue).ToList();

    public IReadOnlyList<SupportCandidate> Infer(IReadOnlyList<ScoreReportAnswer> answers, DateOnly today, int limit)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var weights = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (answer.QuestionCode is not { } question || !_signals.TryGetValue(question, out var needs))
            {
                continue;
            }

            var room = answer.RoomToImprove();
            foreach (var need in needs)
            {
                weights[need] = weights.GetValueOrDefault(need) + room;
            }
        }

        return Current(today)
            .SelectMany(authority => authority.Services.Select(service => (authority, service)))
            .Select(pair =>
            {
                // A need nobody's answers raised does not count, and neither does one at no weight.
                var strongest = pair.service.Needs.Where(need => weights.GetValueOrDefault(need) > 0m).OrderByDescending(need => weights[need]).FirstOrDefault();
                return strongest is null ? null : new SupportCandidate(pair.authority, pair.service, _needs[strongest], weights[strongest]);
            })
            .OfType<SupportCandidate>()
            .OrderByDescending(candidate => candidate.Weight)
            .Take(limit)
            .ToList();
    }

    /// <summary>Reads and checks the file. Throws naming the entry and the problem, never a value that could be sensitive.</summary>
    public static SupportKnowledgeGraph Parse(string json)
    {
        var file = JsonSerializer.Deserialize<GraphFile>(json, Options)
            ?? throw new InvalidOperationException("The support knowledge graph is empty.");
        if (file.Schema != Schema || file.Needs is null || file.Signals is null || file.Authorities is null)
        {
            throw new InvalidOperationException($"The support knowledge graph must state schema {Schema}, needs, signals and authorities.");
        }

        var needs = new Dictionary<string, SupportNeed>(StringComparer.Ordinal);
        for (var index = 0; index < file.Needs.Count; index++)
        {
            var need = file.Needs[index];
            if (string.IsNullOrWhiteSpace(need.Code) || !CodePattern().IsMatch(need.Code) || !IsText(need.Label) || !needs.TryAdd(need.Code, need))
            {
                throw new InvalidOperationException($"Support need {index + 1} in the graph is invalid: it needs a unique snake case code and a label in English and Arabic.");
            }
        }

        foreach (var (question, linked) in file.Signals)
        {
            if (linked is not { Count: > 0 } || !linked.All(needs.ContainsKey))
            {
                throw new InvalidOperationException($"Question {question} in the graph is invalid: it must lead to needs the graph holds.");
            }
        }

        var authorities = new HashSet<string>(file.Authorities.Select(authority => authority.Code ?? string.Empty), StringComparer.Ordinal);
        var authorityCodes = new HashSet<string>(StringComparer.Ordinal);
        var serviceCodes = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < file.Authorities.Count; index++)
        {
            var authority = file.Authorities[index];
            var problem = Problem(authority) ?? ServicesProblem(authority, needs, authorities, serviceCodes);
            if (problem is null && !authorityCodes.Add(authority.Code))
            {
                problem = "its code is used twice";
            }

            if (problem is not null)
            {
                throw new InvalidOperationException($"Support authority {index + 1} in the graph is invalid: {problem}.");
            }
        }

        return new SupportKnowledgeGraph(file.Authorities, needs, file.Signals.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal));
    }

    private static string? Problem(SupportAuthority authority)
    {
        if (string.IsNullOrWhiteSpace(authority.Code) || !CodePattern().IsMatch(authority.Code))
        {
            return "its code must be snake case";
        }

        if (authority.Kind is null || !Kinds.Contains(authority.Kind))
        {
            return "its kind must be government, public_provider or private";
        }

        if (string.IsNullOrWhiteSpace(authority.ApprovalReference))
        {
            return "it has no approval reference";
        }

        if (!IsText(authority.Name) || !IsText(authority.Purpose))
        {
            return "its name and purpose need English and Arabic";
        }

        if (authority.Emirates is not { Count: > 0 } || !authority.Emirates.All(Coverage.Contains))
        {
            return "it must name the emirates it serves";
        }

        if (!IsLink(authority.Website))
        {
            return "its website must be https";
        }

        if (authority.Phone is not null && !PhonePattern().IsMatch(authority.Phone))
        {
            return "its phone may hold only digits, spaces and a leading plus";
        }

        if (authority.Sources is not { Count: > 0 } || !authority.Sources.All(source => source is not null && IsLink(source)))
        {
            return "it needs at least one https source";
        }

        if (authority.CheckedOn == default || authority.ReviewDue <= authority.CheckedOn)
        {
            return "its review date must come after the date it was checked";
        }

        return authority.Services is null ? "it must list its services, even if none" : null;
    }

    private static string? ServicesProblem(
        SupportAuthority authority, Dictionary<string, SupportNeed> needs, HashSet<string> authorities, HashSet<string> serviceCodes)
    {
        foreach (var service in authority.Services)
        {
            if (string.IsNullOrWhiteSpace(service.Code) || !CodePattern().IsMatch(service.Code))
            {
                return "a service's code must be snake case";
            }

            if (!serviceCodes.Add(service.Code))
            {
                return $"service {service.Code} is used twice";
            }

            if (!IsText(service.Name) || !IsText(service.Summary))
            {
                return $"service {service.Code} needs a name and summary in English and Arabic";
            }

            if (service.Needs is not { Count: > 0 } || !service.Needs.All(needs.ContainsKey))
            {
                return $"service {service.Code} must address needs the graph holds";
            }

            if (service.Procedure is null || !service.Procedure.All(IsText))
            {
                return $"each procedure step of service {service.Code} needs English and Arabic";
            }

            if (service.Documents is null || !service.Documents.All(IsText))
            {
                return $"each document of service {service.Code} needs English and Arabic";
            }

            if (!IsLink(service.Website))
            {
                return $"service {service.Code}'s website must be https";
            }

            if (service.Via is not null && !authorities.Contains(service.Via))
            {
                return $"service {service.Code} goes through an authority the graph does not hold";
            }

            if (service.Evidence is null || !Evidence.Contains(service.Evidence))
            {
                return $"service {service.Code} must say how its facts were seen";
            }

            if (service.Sources is not { Count: > 0 } || !service.Sources.All(source => source is not null && IsLink(source)))
            {
                return $"service {service.Code} needs at least one https source";
            }
        }

        return null;
    }

    private static bool IsLink(string? link) =>
        link is null || (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsText(TextDto? text) =>
        text is not null && !string.IsNullOrWhiteSpace(text.En) && !string.IsNullOrWhiteSpace(text.Ar) && ArabicPattern().IsMatch(text.Ar);

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^\+?[0-9][0-9 ]*$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\p{IsArabic}")]
    private static partial Regex ArabicPattern();

    private sealed record GraphFile(string? Schema, List<SupportNeed>? Needs, Dictionary<string, List<string>>? Signals, List<SupportAuthority>? Authorities);
}
