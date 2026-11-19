namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What a report model receives to write a report's summary: the language, each area's points so far and each question
/// with the chosen answer and its points. No total, band, names or IDs. Sensitive personal information: never log it.
/// <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record ScoreReportSummaryFacts(string Language, IReadOnlyList<ScoreReportArea> Areas, IReadOnlyList<ScoreReportAnswer> Answers)
{
    public override string ToString() =>
        $"ScoreReportSummaryFacts {{ Language = {Language}, Areas = {Areas.Count}, Answers = {Answers.Count} }}";
}
