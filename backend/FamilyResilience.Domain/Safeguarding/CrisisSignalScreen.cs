namespace FamilyResilience.Domain.Safeguarding;

/// <summary>
/// The deterministic phrase screen: whole-token sequence matching of normalised text against the list. For Arabic the
/// first word of a phrase may carry one leading clitic (و or ف "and", ب or س the future or a preposition), or و or ف
/// followed by ب or س, when at least three letters remain after it. An occurrence that shares a word with an occurrence
/// of one of the rule's exceptions does not count. When several rules match, the concern order decides (suicide or
/// self-harm, then abuse or danger, then harm to others); within a concern the first rule in file order wins.
/// </summary>
public sealed class CrisisSignalScreen
{
    private const int MinLettersAfterPrefix = 3;

    private readonly IReadOnlyList<CrisisSignalRule> _rules;
    private readonly string[][][] _tokens;
    private readonly string[][][] _exceptions;

    public CrisisSignalScreen(CrisisSignalList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        Version = list.Version;
        _rules = list.Rules;
        _tokens = [.. list.Rules.Select(r => Split(r.Phrases))];
        _exceptions = [.. list.Rules.Select(r => Split(r.Exceptions))];
    }

    public string Version { get; }

    public CrisisSignalMatch? Screen(string? text)
    {
        var words = CrisisTextNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        CrisisSignalRule? best = null;
        var bestRank = int.MaxValue;
        for (var i = 0; i < _rules.Count; i++)
        {
            var rank = Rank(_rules[i].Concern);
            if (rank >= bestRank || !Matches(words, _tokens[i], _exceptions[i]))
            {
                continue;
            }

            best = _rules[i];
            bestRank = rank;
        }

        return best is null ? null : new CrisisSignalMatch(best.Code, best.Concern);
    }

    private static string[][] Split(IReadOnlyList<string> phrases) =>
        [.. phrases.Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(tokens => tokens.Length > 0)];

    private static int Rank(SafetyConcern concern) => concern switch
    {
        SafetyConcern.SuicideOrSelfHarm => 0,
        SafetyConcern.AbuseOrDanger => 1,
        SafetyConcern.HarmToOthers => 2,
        _ => 3,
    };

    /// <summary>True when some phrase occurs in <paramref name="words"/> without an exception overlapping it.</summary>
    private static bool Matches(string[] words, string[][] phrases, string[][] exceptions)
    {
        foreach (var phrase in phrases)
        {
            for (var start = 0; start + phrase.Length <= words.Length; start++)
            {
                if (MatchesAt(words, phrase, start) && !Excepted(words, exceptions, start, phrase.Length))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>True when an exception occurs sharing a word with the occurrence at <paramref name="start"/>.</summary>
    private static bool Excepted(string[] words, string[][] exceptions, int start, int length)
    {
        foreach (var exception in exceptions)
        {
            var last = Math.Min(start + length - 1, words.Length - exception.Length);
            for (var at = Math.Max(0, start - exception.Length + 1); at <= last; at++)
            {
                if (MatchesAt(words, exception, at))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MatchesAt(string[] words, string[] phrase, int start)
    {
        for (var j = 1; j < phrase.Length; j++)
        {
            if (words[start + j] != phrase[j])
            {
                return false;
            }
        }

        return FirstWordMatches(words[start], phrase[0]);
    }

    private static bool FirstWordMatches(string word, string first)
    {
        if (word == first)
        {
            return true;
        }

        // One clitic: و or ف (and), ب or س (future, or a preposition).
        if (word.Length > MinLettersAfterPrefix && (word[0] is 'و' or 'ف' or 'ب' or 'س') && word.AsSpan(1).SequenceEqual(first))
        {
            return true;
        }

        // و or ف, then ب or س: "وسأنتحر", "فبيضربني".
        return word.Length > MinLettersAfterPrefix + 1
            && (word[0] is 'و' or 'ف')
            && (word[1] is 'ب' or 'س')
            && word.AsSpan(2).SequenceEqual(first);
    }
}
