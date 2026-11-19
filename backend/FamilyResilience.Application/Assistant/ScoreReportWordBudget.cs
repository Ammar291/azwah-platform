namespace FamilyResilience.Application.Assistant;

/// <summary>
/// How long a report's parts may be (D-145): the owner asked for a report about 80 % shorter, precise and compact. The prompts
/// ask for these budgets, and <see cref="Trim"/> enforces them on whatever the model wrote, ending at the last whole sentence
/// that fits, because a model's own count cannot be trusted.
/// </summary>
public static class ScoreReportWordBudget
{
    public const int SummaryWords = 30;
    public const int TitleWords = 6;
    public const int DetailWords = 16;
    public const int AreaWords = 6;
    public const int ReasonWords = 16;
    public const int StepWords = 12;

    /// <summary>Things to start at home, per next step.</summary>
    public const int MaxSteps = 2;

    /// <summary>A whole report's lists; a report written in parts keeps as many, chosen by where each area stands.</summary>
    public const int MaxStrengths = 2;

    public const int MaxWeaknesses = 2;

    public const int MaxNextSteps = 3;

    private const string Ellipsis = "…";

    private static readonly char[] SentenceEnds = ['.', '!', '?', '؟', '۔'];

    /// <summary>The words of <paramref name="text"/>, split on white space.</summary>
    public static int Count(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// <paramref name="text"/> within <paramref name="maxWords"/>: as written when it fits; otherwise up to the last sentence
    /// that ends within the budget; and when even the first sentence is longer, its first words with an ellipsis.
    /// </summary>
    public static string Trim(string? text, int maxWords)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWords);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
        {
            return text.Trim();
        }

        for (var last = maxWords - 1; last >= 0; last--)
        {
            if (EndsSentence(words[last]))
            {
                return string.Join(' ', words.Take(last + 1));
            }
        }

        return string.Join(' ', words.Take(maxWords)).TrimEnd(',', '،', ';', ':') + Ellipsis;
    }

    private static bool EndsSentence(string word) =>
        word.TrimEnd('"', '\'', ')', '”', '’', '»').LastOrDefault() is var mark && SentenceEnds.Contains(mark);

    internal static ScoreReportPoint Point(ScoreReportPoint point) =>
        new(Trim(point.Title, TitleWords), Trim(point.Detail, DetailWords));

    internal static ScoreReportImprovement Improvement(ScoreReportImprovement improvement) =>
        new(Trim(improvement.Area, AreaWords), Trim(improvement.Reason, ReasonWords), [.. improvement.Steps.Take(MaxSteps).Select(step => Trim(step, StepWords))])
        {
            SectionCode = improvement.SectionCode,
            SupportCodes = improvement.SupportCodes,
        };
}
