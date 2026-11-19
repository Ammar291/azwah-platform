using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// Staff scoring of a submitted session (for example one submitted before its scoring rule set was approved). The score
/// and the session's move to Scored commit together, then the approved referral rules route it, inside one
/// transaction, so a failure leaves no partial result. Scoring again with the same rule set returns the existing
/// result, audited as a read.
/// </summary>
public sealed class ScoreSession(
    IAssessmentSessionRepository sessions,
    SessionScoringService scoring,
    ReferralRouter router,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog)
{
    public Task<Result<ScoreSessionOutcome>> HandleAsync(Guid sessionId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(token => ScoreAsync(sessionId, token), cancellationToken);

    private async Task<Result<ScoreSessionOutcome>> ScoreAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(AssessmentSessionId.From(sessionId), cancellationToken);
        if (session is null)
        {
            return Result.Failure<ScoreSessionOutcome>(SessionErrors.SessionNotFound);
        }

        var attempt = await scoring.ScoreAsync(session, cancellationToken);
        if (attempt.IsFailure)
        {
            return Result.Failure<ScoreSessionOutcome>(attempt.Error!);
        }

        if (!attempt.Value.Created)
        {
            // Returning a result reads scores, so it is audited like a listing.
            var read = await AuditedCommit.RunAsync(
                auditLog, unitOfWork, "scoring.score_results.read", "assessment_session", sessionId, cancellationToken);
            return read.IsFailure
                ? Result.Failure<ScoreSessionOutcome>(read.Error!)
                : Result.Success(new ScoreSessionOutcome(attempt.Value.Result.ToResponse(), Created: false));
        }

        // The score commits before routing: the database refuses a referral until the session row says Scored.
        var scored = await unitOfWork.CommitAsync(cancellationToken);
        if (scored.IsFailure)
        {
            return Result.Failure<ScoreSessionOutcome>(scored.Error!);
        }

        var routed = await router.RouteAsync(session, attempt.Value.Result, attempt.Value.SectionCode, cancellationToken);
        if (routed.IsFailure)
        {
            return Result.Failure<ScoreSessionOutcome>(routed.Error!);
        }

        var committed = await unitOfWork.CommitAsync(cancellationToken);
        return committed.IsFailure
            ? Result.Failure<ScoreSessionOutcome>(committed.Error!)
            : Result.Success(new ScoreSessionOutcome(attempt.Value.Result.ToResponse(), Created: true));
    }
}
