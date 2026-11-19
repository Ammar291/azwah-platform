using FamilyResilience.Application.Scoring;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What a report model receives for one scored session, and nothing more: the language, the recorded total and band, the
/// scale, the area scores, and each question with the chosen answer and its points. No names, roster, household
/// reference or IDs. Sensitive personal information: never log it. <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record ScoreReportFacts(
    string Language,
    decimal TotalScore,
    int Minimum,
    int Maximum,
    string BandCode,
    IReadOnlyList<ScoreBandResponse> Bands,
    IReadOnlyList<ScoreReportArea> Areas,
    IReadOnlyList<ScoreReportAnswer> Answers)
{
    /// <summary>The services the knowledge graph reached from these answers, by area (D-115).</summary>
    public IReadOnlyList<ScoreReportSupportOption> SupportOptions { get; init; } = [];

    public override string ToString() =>
        $"ScoreReportFacts {{ Language = {Language}, Areas = {Areas.Count}, Answers = {Answers.Count} }}";
}
