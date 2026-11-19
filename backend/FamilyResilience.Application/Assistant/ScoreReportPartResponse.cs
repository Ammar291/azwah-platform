namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One area's part of a report, written ahead of the score: a model's text, with the approved catalogue's authorities in
/// <see cref="NextSteps"/>. The server keeps no copy. <see cref="Standing"/> says where the area's points sit between its
/// lowest and highest possible, as a next step's does (D-114): the app keeps the strengths of the areas standing highest
/// and the weaknesses and steps of those standing lowest (D-145), and never shows it.
/// </summary>
public sealed record ScoreReportPartResponse(
    string SectionCode,
    string Language,
    IReadOnlyList<ScoreReportPoint> Strengths,
    IReadOnlyList<ScoreReportPoint> Weaknesses,
    IReadOnlyList<ScoreReportNextStep> NextSteps,
    string ModelName,
    DateTimeOffset GeneratedAt,
    decimal Standing);
