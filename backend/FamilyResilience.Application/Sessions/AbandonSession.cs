using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Sessions;

/// <summary>Abandons a session in progress, keeping it as history, so a new one can start. No endpoint yet.</summary>
public sealed class AbandonSession(
    IAssessmentSessionRepository sessions,
    IAssessmentAssignmentRepository assignments,
    IInstrumentVersionRepository versions,
    IParticipantAccess access,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<SessionResponse>> HandleAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var loaded = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<SessionResponse>(loaded.Error!);
        }

        var session = loaded.Value;
        var assignment = await assignments.GetAsync(session.AssessmentAssignmentId, cancellationToken)
            ?? throw new InvalidOperationException("A session's assignment must exist.");
        var abandoned = session.Abandon(assignment, timeProvider.GetUtcNow());
        if (abandoned.IsFailure)
        {
            return Result.Failure<SessionResponse>(abandoned.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "sessions.session.abandoned", "assessment_session", session.Id.Value, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<SessionResponse>(committed.Error!);
        }

        var version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        return Result.Success(session.ToResponse(version));
    }
}
