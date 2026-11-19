using System.Text;
using System.Text.RegularExpressions;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// Turns what a person typed into a short list of clean search terms. Only letters survive, so digits, symbols and
/// punctuation can never reach the query. The Arabic steps mirror the document-side SQL step for step.
/// </summary>
public static partial class SupportResourceQueryText
{
    public const int MaxTextLength = 200;
    public const int MaxTerms = 8;
    private const int MaxTermLength = 64;

    // Longest first, so "ها" wins over "ه".
    private static readonly string[] Suffixes =
        ["ها", "هم", "هن", "كم", "كن", "نا", "ات", "ان", "ون", "ين", "يه", "ه", "ي", "ك"];

    public static IReadOnlyList<SearchTerm> Parse(string? text)
    {
        var terms = new List<SearchTerm>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return terms;
        }

        foreach (var (run, kind) in SplitRuns(text))
        {
            var term = kind == SearchTermKind.Arabic ? ReduceArabic(run) : ReduceEnglish(run);
            if (term is not null && !terms.Contains(term))
            {
                terms.Add(term);
                if (terms.Count == MaxTerms)
                {
                    break;
                }
            }
        }

        return terms;
    }

    /// <summary>Folds, strips diacritics and tatweel, then strips one leading article when two letters remain.</summary>
    public static string NormaliseArabic(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return StripArticle(Fold(word));
    }

    private static string Fold(string word)
    {
        var translated = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            translated.Append(c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => c,
            });
        }

        return Diacritics().Replace(translated.ToString(), string.Empty);
    }

    private static string StripArticle(string folded) => Article().Replace(folded, "$1");

    private static SearchTerm? ReduceArabic(string run)
    {
        var folded = Fold(run);
        if (ArabicFunctionWords.All.Contains(folded))
        {
            return null;
        }

        var word = StripArticle(folded);
        if (ArabicFunctionWords.All.Contains(word))
        {
            return null;
        }

        foreach (var suffix in Suffixes)
        {
            if (word.Length - suffix.Length >= 3 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                word = word[..^suffix.Length];
                break;
            }
        }

        // Checked once more: "علينا" and "عندهم" reduce to the function words "علي" and "عند", and "علي" would
        // prefix-match every document with "على". Query side only; the documents keep every word.
        if (ArabicFunctionWords.All.Contains(word))
        {
            return null;
        }

        return word.Length < 2 ? null : new SearchTerm(Cut(word), SearchTermKind.Arabic);
    }

    // PostgreSQL drops English stop words itself; dropping them here keeps them from using up the term budget.
    private static SearchTerm? ReduceEnglish(string run)
    {
        var word = run.ToLowerInvariant();
        return word.Length < 2 || EnglishStopWords.All.Contains(word)
            ? null
            : new SearchTerm(Cut(word), SearchTermKind.English);
    }

    private static string Cut(string word) => word.Length > MaxTermLength ? word[..MaxTermLength] : word;

    private static IEnumerable<(string Run, SearchTermKind Kind)> SplitRuns(string text)
    {
        var run = new StringBuilder();
        SearchTermKind? current = null;
        foreach (var c in text)
        {
            SearchTermKind? kind = IsArabic(c) ? SearchTermKind.Arabic : IsLatinLetter(c) ? SearchTermKind.English : null;
            if (kind != current && run.Length > 0 && current is { } finished)
            {
                yield return (run.ToString(), finished);
                run.Clear();
            }

            current = kind;
            if (kind is not null)
            {
                run.Append(c);
            }
        }

        if (run.Length > 0 && current is { } last)
        {
            yield return (run.ToString(), last);
        }
    }

    private static bool IsArabic(char c) =>
        c is >= 'ء' and <= 'ي' or >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ' or 'ٱ';

    private static bool IsLatinLetter(char c) =>
        char.IsLetter(c) && (c <= 'ɏ' || c is >= 'Ḁ' and <= 'ỿ');

    [GeneratedRegex("[\\u064B-\\u065F\\u0670\\u0640]")]
    private static partial Regex Diacritics();

    [GeneratedRegex("(^|[^ء-ي])(وال|فال|بال|كال|لل|ال)(?=[ء-ي]{2,})")]
    private static partial Regex Article();
}
