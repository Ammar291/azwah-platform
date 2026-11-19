using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// The session's latest score result, or "pending" while unscored. Only the household's head gets the score values;
/// anyone else who may act for the member gets the state without them (D-103). The head also gets the grading scale of
/// a complete result (D-111). Reading a result is audited.
/// </summary>
public sealed class GetSessionResult(
    IAssessmentSessionRepository sessions,
    IScoreResultRepository scoreResults,
    ScoreScaleReader scales,
    IParticipantAccess access,
    IAuditLog auditLog)
{
    public async Task<Result<SessionResultResponse>> HandleAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var loaded = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<SessionResultResponse>(loaded.Error!);
        }

        var session = loaded.Value;
        if (session.Status is not (SessionStatus.Submitted or SessionStatus.Scored))
        {
            return Result.Failure<SessionResultResponse>(SessionErrors.NotSubmitted);
        }

        var latest = (await scoreResults.ListForSessionAsync(session.Id, cancellationToken)).MaxBy(result => result.ComputedAt);
        var visibility = await access.IsHeadOfHouseholdAsync(cancellationToken) ? ScoreVisibility.Visible : ScoreVisibility.HeadOfHouseholdOnly;
        var shown = visibility == ScoreVisibility.Visible;
        var scale = shown && latest is { Status: ScoreStatus.Complete } ? await scales.ReadAsync(latest, cancellationToken) : null;
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("scoring.score_results.read", "assessment_session", sessionId.ToString(), AuditOutcome.Succeeded), cancellationToken);
        if (recorded.IsFailure)
        {
            return Result.Failure<SessionResultResponse>(recorded.Error!);
        }

        return Result.Success(latest is null
            ? new SessionResultResponse(session.Id.Value, session.Status, ResultState.Pending, null, null, null, [], null, visibility, null)
            : new SessionResultResponse(
                session.Id.Value,
                session.Status,
                latest.Status == ScoreStatus.Complete ? ResultState.Complete : ResultState.Incomplete,
                latest.IncompleteReason,
                shown ? latest.TotalScore : null,
                shown ? latest.TierCode : null,
                shown ? latest.CategoryScores.ToResponses() : [],
                latest.ComputedAt,
                visibility,
                scale));
    }
}
