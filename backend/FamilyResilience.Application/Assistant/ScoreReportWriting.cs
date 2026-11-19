namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The text a model wrote for a report, and only text: it has no field that could carry a score, band or decision. Any
/// list may be empty, because a model must not invent a strength a family's answers do not show, but a report says
/// something. <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record ScoreReportWriting(
    string Summary,
    IReadOnlyList<ScoreReportPoint> Strengths,
    IReadOnlyList<ScoreReportPoint> Weaknesses,
    IReadOnlyList<ScoreReportImprovement> Improvements)
{
    public const int MaxSummaryLength = 3_000;
    public const int MaxTextLength = 1_500;
    public const int MaxEntries = 8;

    /// <summary>Whether the text has a summary, at least one entry, and stays within the limits a phone is built for.</summary>
    public bool IsUsable() =>
        HasText(Summary, MaxSummaryLength)
        && Strengths.Count + Weaknesses.Count + Improvements.Count > 0
        && Strengths.Count <= MaxEntries
        && Weaknesses.Count <= MaxEntries
        && Improvements.Count <= MaxEntries
        && Strengths.Concat(Weaknesses).All(point => HasText(point.Title) && HasText(point.Detail))
        && Improvements.All(improvement =>
            HasText(improvement.Area)
            && HasText(improvement.Reason)
            && improvement.Steps.Count is > 0 and <= MaxEntries
            && improvement.Steps.All(step => HasText(step)));

    /// <summary>
    /// Within the report's budgets (D-145): the summary, two strengths and two weaknesses, each text cut to its words, and two
    /// steps an improvement. Every improvement stays, because only once the steps are ordered by where their areas stand can
    /// the weakest <see cref="ScoreReportWordBudget.MaxNextSteps"/> be kept.
    /// </summary>
    public ScoreReportWriting Limited() =>
        new(
            ScoreReportWordBudget.Trim(Summary, ScoreReportWordBudget.SummaryWords),
            [.. Strengths.Take(ScoreReportWordBudget.MaxStrengths).Select(ScoreReportWordBudget.Point)],
            [.. Weaknesses.Take(ScoreReportWordBudget.MaxWeaknesses).Select(ScoreReportWordBudget.Point)],
            [.. Improvements.Select(ScoreReportWordBudget.Improvement)]);

    public override string ToString() =>
        $"ScoreReportWriting {{ Strengths = {Strengths.Count}, Weaknesses = {Weaknesses.Count}, Improvements = {Improvements.Count} }}";

    private static bool HasText(string? text, int limit = MaxTextLength) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= limit;
}
