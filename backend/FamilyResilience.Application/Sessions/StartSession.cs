using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Starts a session on an assignment the caller may act for. Checks, in order: the assignment is the caller's to
/// answer (otherwise "not found"), the household is active and the member has participation consent, no session is
/// already live, the version is still published, and the assignment is waiting and unexpired.
/// </summary>
public sealed class StartSession(
    IAssessmentAssignmentRepository assignments,
    IAssessmentSessionRepository sessions,
    IInstrumentVersionRepository versions,
    IHouseholdRepository households,
    IConsentRecordRepository consents,
    IParticipantAccess access,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<SessionResponse>> HandleAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetAsync(AssessmentAssignmentId.From(assignmentId), cancellationToken);
        if (assignment is null || !await access.CanActForMemberAsync(assignment.FamilyMemberId, cancellationToken))
        {
            return Result.Failure<SessionResponse>(AssignmentErrors.AssignmentNotFound);
        }

        var allowed = await ParticipationChecks.EnsureMayAnswerAsync(assignment.FamilyMemberId, households, consents, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<SessionResponse>(allowed.Error!);
        }

        if (await sessions.GetLiveForAssignmentAsync(assignment.Id, cancellationToken) is not null)
        {
            return Result.Failure<SessionResponse>(SessionErrors.SessionAlreadyExists);
        }

        var version = await versions.GetAsync(assignment.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("An assignment's version must exist.");
        if (version.Status != InstrumentVersionStatus.Approved)
        {
            return Result.Failure<SessionResponse>(SessionErrors.VersionNotPublished);
        }

        var started = AssessmentSession.Start(assignment, timeProvider.GetUtcNow());
        if (started.IsFailure)
        {
            return Result.Failure<SessionResponse>(started.Error!);
        }

        sessions.Add(started.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "sessions.session.started", "assessment_session", started.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<SessionResponse>(committed.Error!)
            : Result.Success(started.Value.ToResponse(version));
    }
}
