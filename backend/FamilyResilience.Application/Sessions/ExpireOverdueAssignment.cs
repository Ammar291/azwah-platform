using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Closes one assignment whose time has passed (its wave closed), in one transaction. A session still in progress is
/// locked first, as saving answers and submitting lock it, then abandoned and committed with its audit event; its
/// answers stay as history and are never scored. The assignment is then marked Expired and committed with its own
/// audit event. An assignment that was completed, cancelled or expired meanwhile is left as it is. The platform acts
/// here, not a participant, so there is no access check.
/// </summary>
public sealed class ExpireOverdueAssignment(
    IAssessmentAssignmentRepository assignments,
    IAssessmentSessionRepository sessions,
    ISessionLock sessionLock,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public Task<Result<OverdueAssignmentOutcome>> HandleAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(token => CloseAsync(assignmentId, token), cancellationToken);

    private async Task<Result<OverdueAssignmentOutcome>> CloseAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken)
    {
        // Locked before anything is loaded: a submission or a save that got there first has finished by the time
        // the session is read, and one that comes later waits and then finds the session abandoned.
        if (await sessions.FindLiveIdForAssignmentAsync(assignmentId, cancellationToken) is { } liveSessionId)
        {
            await sessionLock.LockAsync(liveSessionId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var assignment = await assignments.GetAsync(assignmentId, cancellationToken);
        if (assignment is null || !assignment.IsOverdueAt(now))
        {
            return Result.Success(OverdueAssignmentOutcome.Unchanged);
        }

        var outcome = OverdueAssignmentOutcome.Expired;
        var session = await sessions.GetLiveForAssignmentAsync(assignmentId, cancellationToken);
        if (session is { Status: SessionStatus.InProgress })
        {
            var abandoned = session.Abandon(assignment, now);
            if (abandoned.IsFailure)
            {
                return Result.Failure<OverdueAssignmentOutcome>(abandoned.Error!);
            }

            var saved = await AuditedCommit.RunAsync(
                auditLog, unitOfWork, "sessions.session.abandoned", "assessment_session", session.Id.Value, cancellationToken);
            if (saved.IsFailure)
            {
                return Result.Failure<OverdueAssignmentOutcome>(saved.Error!);
            }

            outcome = OverdueAssignmentOutcome.SessionAbandonedAndExpired;
        }

        var expired = assignment.Expire(now);
        if (expired.IsFailure)
        {
            return Result.Failure<OverdueAssignmentOutcome>(expired.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "assignments.assignment.expired", "assessment_assignment", assignment.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<OverdueAssignmentOutcome>(committed.Error!)
            : Result.Success(outcome);
    }
}
