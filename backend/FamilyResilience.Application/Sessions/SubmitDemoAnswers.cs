using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// DEMONSTRATIONS ONLY (D-143). One tap of a demonstration's "low / medium / high" finishes the session: the server chooses
/// the answers the approved rule set scores in that band (<see cref="DemoAnswerPlanner"/>, from its cache after the first
/// time), saves them with <see cref="SaveResponses"/>, then submits, scores and routes with <see cref="SubmitSession"/>.
/// Those are the use cases a person's own answers go through, with their lock, checks and separate commits (answers, then
/// submission, score and routing), so nothing here scores or skips a rule: an expired assignment, missing consent or
/// another family's session is refused exactly as there. Switched off (404) unless <see cref="DemoToolsSettings.Enabled"/>.
/// The <c>Idempotency-Key</c> is the submission's: a retry of a request that went through answers as it did, while a
/// session submitted by any other request answers <c>sessions.not_in_progress</c>. The saved answers are audited.
/// </summary>
public sealed class SubmitDemoAnswers(
    DemoToolsSettings settings,
    IAssessmentSessionRepository sessions,
    IParticipantAccess access,
    IdempotencyGuard idempotency,
    DemoAnswerPlanner planner,
    SaveResponses saveResponses,
    SubmitSession submitSession,
    IAuditLog auditLog)
{
    public const string AuditAction = "sessions.demo_answers.saved";

    public async Task<Result<SubmissionResponse>> HandleAsync(
        Guid sessionId, Guid idempotencyKey, DemoAnswersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.Enabled)
        {
            return Result.Failure<SubmissionResponse>(DemoToolsErrors.Disabled);
        }

        var session = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (session.IsFailure)
        {
            return Result.Failure<SubmissionResponse>(session.Error!);
        }

        // A key already used for something else is refused before anything is saved.
        var earlier = await idempotency.FindAsync(
            idempotencyKey, SubmitSession.Operation, RequestHash.Of(SubmitSession.Operation, sessionId.ToString()), cancellationToken);
        if (earlier.IsFailure)
        {
            return Result.Failure<SubmissionResponse>(earlier.Error!);
        }

        if (session.Value.Status is SessionStatus.Submitted or SessionStatus.Scored)
        {
            return earlier.Value is null
                ? Result.Failure<SubmissionResponse>(SessionErrors.NotInProgress)
                : OnlyOurs(await submitSession.HandleAsync(sessionId, idempotencyKey, cancellationToken));
        }

        if (session.Value.Status != SessionStatus.InProgress)
        {
            return Result.Failure<SubmissionResponse>(SessionErrors.NotInProgress);
        }

        var plan = await planner.PlanAsync(session.Value, DemoAnswersRequestValidator.LevelOf(request), cancellationToken);
        if (plan.IsFailure)
        {
            return Result.Failure<SubmissionResponse>(plan.Error!);
        }

        var answers = plan.Value.Answers.Select(answer => new AnswerRequest(answer.ItemId.Value, answer.OptionId.Value)).ToList();
        var saved = await saveResponses.HandleAsync(sessionId, new SaveResponsesRequest(answers), cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<SubmissionResponse>(saved.Error!);
        }

        var recorded = await auditLog.RecordAsync(
            new AuditRecord(AuditAction, "assessment_session", sessionId.ToString(), AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure
            ? Result.Failure<SubmissionResponse>(recorded.Error!)
            : OnlyOurs(await submitSession.HandleAsync(sessionId, idempotencyKey, cancellationToken));
    }

    /// <summary>Another request submitted the session first (a second tap racing this one): it is no longer in progress.</summary>
    private static Result<SubmissionResponse> OnlyOurs(Result<SubmissionResponse> submitted) =>
        submitted.IsSuccess && submitted.Value.Outcome == SubmissionOutcome.AlreadySubmitted
            ? Result.Failure<SubmissionResponse>(SessionErrors.NotInProgress)
            : submitted;
}
