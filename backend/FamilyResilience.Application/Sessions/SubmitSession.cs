using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Submits a session, then scores and routes it, all in one transaction. The session row is locked first, so a
/// concurrent submission waits. The steps commit in order because the database checks each one against the last:
/// submit (with its audit and idempotency key), score with the approved rule set for the scope, route with the
/// approved referral rules. Any failure rolls every step back, so a session is never left Submitted without the result
/// it should have. With no approved scoring rule set the session stays Submitted and its result reads "pending".
/// A repeated submission changes nothing and reports <see cref="SubmissionOutcome.AlreadySubmitted"/>. Its key is not
/// stored: only a request that actually submitted is remembered, so a replay answers "submitted" for that request alone.
/// </summary>
public sealed class SubmitSession(
    IAssessmentSessionRepository sessions,
    IAssessmentAssignmentRepository assignments,
    IInstrumentVersionRepository versions,
    IHouseholdRepository households,
    IConsentRecordRepository consents,
    IParticipantAccess access,
    ISessionLock sessionLock,
    IdempotencyGuard idempotency,
    SessionScoringService scoring,
    ReferralRouter router,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const string Operation = "sessions.submit";

    public Task<Result<SubmissionResponse>> HandleAsync(Guid sessionId, Guid idempotencyKey, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(token => SubmitAsync(sessionId, idempotencyKey, token), cancellationToken);

    private async Task<Result<SubmissionResponse>> SubmitAsync(Guid sessionId, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        await sessionLock.LockAsync(AssessmentSessionId.From(sessionId), cancellationToken);
        var requestHash = RequestHash.Of(Operation, sessionId.ToString());
        var earlier = await idempotency.FindAsync(idempotencyKey, Operation, requestHash, cancellationToken);
        if (earlier.IsFailure)
        {
            return Result.Failure<SubmissionResponse>(earlier.Error!);
        }

        var loaded = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<SubmissionResponse>(loaded.Error!);
        }

        var session = loaded.Value;
        if (earlier.Value is not null)
        {
            // A retry of a request that already succeeded: answer as it did, from the session as it is now.
            return Result.Success(Response(session, SubmissionOutcome.Submitted));
        }

        if (session.Status is SessionStatus.Submitted or SessionStatus.Scored)
        {
            // Another request submitted it. Nothing changes and the key is not stored, so a replay of this request
            // reaches this branch again rather than answering "submitted".
            return Result.Success(Response(session, SubmissionOutcome.AlreadySubmitted));
        }

        var submitted = await SubmitCoreAsync(session, cancellationToken);
        if (submitted.IsFailure)
        {
            return Result.Failure<SubmissionResponse>(submitted.Error!);
        }

        idempotency.Remember(idempotencyKey, Operation, sessionId, requestHash);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "sessions.session.submitted", "assessment_session", sessionId, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<SubmissionResponse>(committed.Error!);
        }

        var scored = await ScoreAndRouteAsync(session, cancellationToken);
        return scored.IsFailure
            ? Result.Failure<SubmissionResponse>(scored.Error!)
            : Result.Success(Response(session, SubmissionOutcome.Submitted));
    }

    private async Task<Result> SubmitCoreAsync(AssessmentSession session, CancellationToken cancellationToken)
    {
        var allowed = await ParticipationChecks.EnsureMayAnswerAsync(session.FamilyMemberId, households, consents, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var assignment = await assignments.GetAsync(session.AssessmentAssignmentId, cancellationToken)
            ?? throw new InvalidOperationException("A session's assignment must exist.");
        var version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        var outcome = session.Submit(version, assignment, timeProvider.GetUtcNow());
        return outcome.IsFailure ? Result.Failure(outcome.Error!) : Result.Success();
    }

    /// <summary>Scores and commits, then routes and commits. No approved scoring rule set is not a failure.</summary>
    private async Task<Result> ScoreAndRouteAsync(AssessmentSession session, CancellationToken cancellationToken)
    {
        var attempt = await scoring.ScoreAsync(session, cancellationToken);
        if (attempt.IsFailure)
        {
            return attempt.Error == ScoringErrors.PolicyNotApproved ? Result.Success() : Result.Failure(attempt.Error!);
        }

        var scored = await unitOfWork.CommitAsync(cancellationToken);
        if (scored.IsFailure)
        {
            return scored;
        }

        var routed = await router.RouteAsync(session, attempt.Value.Result, attempt.Value.SectionCode, cancellationToken);
        return routed.IsFailure ? Result.Failure(routed.Error!) : await unitOfWork.CommitAsync(cancellationToken);
    }

    private static SubmissionResponse Response(AssessmentSession session, SubmissionOutcome outcome) =>
        new(session.Id.Value, outcome, session.Status, session.SubmittedAt ?? throw new InvalidOperationException("A submitted session has a submission time."));
}
