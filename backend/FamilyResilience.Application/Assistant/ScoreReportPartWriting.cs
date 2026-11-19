namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The text a model wrote about one area, and only text. A list may be empty, because a model must not invent a strength
/// the answers do not show, but a part says something. <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record ScoreReportPartWriting(
    IReadOnlyList<ScoreReportPoint> Strengths,
    IReadOnlyList<ScoreReportPoint> Weaknesses,
    IReadOnlyList<ScoreReportImprovement> Improvements)
{
    /// <summary>One strength, one weakness and one improvement an area at most (D-145; two before).</summary>
    public const int MaxEntries = 1;

    /// <summary>Whether it holds at least one entry, and stays within the limits a whole report keeps to.</summary>
    public bool IsUsable() =>
        Strengths.Count + Weaknesses.Count + Improvements.Count > 0
        && Strengths.Count <= MaxEntries
        && Weaknesses.Count <= MaxEntries
        && Improvements.Count <= MaxEntries
        && new ScoreReportWriting("-", Strengths, Weaknesses, Improvements).IsUsable();

    /// <summary>
    /// The first entries of each list, within the limit, each text within its word budget (<see cref="ScoreReportWordBudget"/>).
    /// Our schema doesn't cap a list, and gpt-5.4-mini sometimes writes one too many (measured: 7 in 103 parts, D-116). The
    /// rest of its text is fine, so a call isn't wasted and retried over that.
    /// </summary>
    public ScoreReportPartWriting Limited() =>
        new(
            [.. Strengths.Take(MaxEntries).Select(ScoreReportWordBudget.Point)],
            [.. Weaknesses.Take(MaxEntries).Select(ScoreReportWordBudget.Point)],
            [.. Improvements.Take(MaxEntries).Select(ScoreReportWordBudget.Improvement)]);

    public override string ToString() =>
        $"ScoreReportPartWriting {{ Strengths = {Strengths.Count}, Weaknesses = {Weaknesses.Count}, Improvements = {Improvements.Count} }}";
}
