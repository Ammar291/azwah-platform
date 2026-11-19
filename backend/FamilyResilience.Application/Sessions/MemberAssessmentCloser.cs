using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <inheritdoc cref="IMemberAssessmentCloser"/>
public sealed class MemberAssessmentCloser(
    IAssessmentSessionRepository sessions,
    IAssessmentAssignmentRepository assignments,
    ISessionLock sessionLock,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog) : IMemberAssessmentCloser
{
    public async Task<Result<MemberAssessmentsClosed>> CloseAsync(
        FamilyMemberId memberId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Locked before anything is loaded, in identifier order: a save or a submission that got there first has finished
        // by the time the session is read, and one that comes later waits and then finds it abandoned. The sessions are
        // listed again once the assignments are read, so one started in between is closed too; one started later
        // changes an assignment read here, and the commit refuses it as a concurrency conflict.
        var inProgress = new List<AssessmentSessionId>();
        await LockInProgressAsync(memberId, inProgress, cancellationToken);
        var open = await assignments.ListOpenForMemberAsync(memberId, cancellationToken);
        await LockInProgressAsync(memberId, inProgress, cancellationToken);

        var abandoned = 0;
        foreach (var sessionId in inProgress)
        {
            var session = await sessions.GetAsync(sessionId, cancellationToken)
                ?? throw new InvalidOperationException("A listed session must exist.");
            if (session.Status != SessionStatus.InProgress)
            {
                continue; // Submitted while this request waited for the lock: it stays as it is.
            }

            var assignment = await assignments.GetAsync(session.AssessmentAssignmentId, cancellationToken)
                ?? throw new InvalidOperationException("A session's assignment must exist.");
            var closed = session.Abandon(assignment, now);
            var staged = closed.IsSuccess ? Stage("sessions.session.abandoned", "assessment_session", session.Id.Value) : closed;
            if (staged.IsFailure)
            {
                return Result.Failure<MemberAssessmentsClosed>(staged.Error!);
            }

            abandoned++;
        }

        // The sessions first: abandoning one returns its assignment to waiting, and the assignments are then cancelled.
        if (abandoned > 0 && await unitOfWork.CommitAsync(cancellationToken) is { IsFailure: true } sessionsSaved)
        {
            return Result.Failure<MemberAssessmentsClosed>(sessionsSaved.Error!);
        }

        foreach (var assignment in open)
        {
            var cancelled = assignment.Cancel(now);
            var staged = cancelled.IsSuccess ? Stage("assignments.assignment.cancelled", "assessment_assignment", assignment.Id.Value) : cancelled;
            if (staged.IsFailure)
            {
                return Result.Failure<MemberAssessmentsClosed>(staged.Error!);
            }
        }

        if (open.Count > 0 && await unitOfWork.CommitAsync(cancellationToken) is { IsFailure: true } assignmentsSaved)
        {
            return Result.Failure<MemberAssessmentsClosed>(assignmentsSaved.Error!);
        }

        return Result.Success(new MemberAssessmentsClosed(abandoned, open.Count));
    }

    private async Task LockInProgressAsync(FamilyMemberId memberId, List<AssessmentSessionId> locked, CancellationToken cancellationToken)
    {
        foreach (var sessionId in await sessions.ListInProgressIdsForMemberAsync(memberId, cancellationToken))
        {
            if (!locked.Contains(sessionId))
            {
                await sessionLock.LockAsync(sessionId, cancellationToken);
                locked.Add(sessionId);
            }
        }
    }

    private Result Stage(string action, string resourceType, Guid resourceId) =>
        auditLog.Stage(new AuditRecord(action, resourceType, resourceId.ToString(), AuditOutcome.Succeeded));
}
