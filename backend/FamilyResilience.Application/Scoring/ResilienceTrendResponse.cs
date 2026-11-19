using FamilyResilience.Application.Sessions;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// The family's resilience over time (D-132): the household assessment's recorded totals, oldest first, on the scale of
/// the latest one. Only the household's head gets the scale and the points (D-103); everyone else gets
/// <see cref="ScoreVisibility.HeadOfHouseholdOnly"/> and nothing else. <see cref="NotShownCount"/> counts the submitted
/// check-ins that are not drawn: no total recorded yet, an incomplete result, or another scale. Clients draw it; they
/// never compute a score, a band or a change from it.
/// </summary>
public sealed record ResilienceTrendResponse(
    ScoreVisibility ScoreVisibility,
    ScoreScaleResponse? Scale,
    IReadOnlyList<ResilienceTrendPointResponse> Points,
    int NotShownCount);
