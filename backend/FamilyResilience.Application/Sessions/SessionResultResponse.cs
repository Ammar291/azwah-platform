using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// The latest result of a session for the member or the person answering for them: totals, tier and category scores as
/// the approved rule set computed them, for the household's head only (<see cref="ScoreVisibility"/>, D-103), with the
/// grading scale they sit on (D-111). Clients display it; they never compute it.
/// </summary>
public sealed record SessionResultResponse(
    Guid SessionId,
    SessionStatus SessionStatus,
    ResultState State,
    string? IncompleteReason,
    decimal? TotalScore,
    string? TierCode,
    IReadOnlyList<CategoryScoreResponse> CategoryScores,
    DateTimeOffset? ComputedAt,
    ScoreVisibility ScoreVisibility,
    ScoreScaleResponse? Scale);
