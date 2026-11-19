namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What a report model receives to write about one area while the person is still answering: the language, the area's name
/// and each question with the chosen answer and its points. No score, band, names or IDs. Sensitive personal
/// information: never log it. <see cref="ToString"/> prints counts only. <see cref="SupportOptions"/> are the services the
/// knowledge graph reached from these answers (D-115); the options are public, but which ones were reached is about a family.
/// </summary>
public sealed record ScoreReportPartFacts(string Language, string Area, IReadOnlyList<ScoreReportAnswer> Answers)
{
    public IReadOnlyList<ScoreReportSupportOption> SupportOptions { get; init; } = [];

    public override string ToString() => $"ScoreReportPartFacts {{ Language = {Language}, Answers = {Answers.Count} }}";
}
