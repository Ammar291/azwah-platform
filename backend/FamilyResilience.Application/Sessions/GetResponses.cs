using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Sessions;

/// <summary>The session's answers, for the member or the person answering for them. Every read is audited.</summary>
public sealed class GetResponses(
    IAssessmentSessionRepository sessions, IInstrumentVersionRepository versions, IParticipantAccess access, IAuditLog auditLog)
{
    public async Task<Result<SessionAnswersResponse>> HandleAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (session.IsFailure)
        {
            return Result.Failure<SessionAnswersResponse>(session.Error!);
        }

        var version = await versions.GetAsync(session.Value.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("sessions.responses.read", "assessment_session", sessionId.ToString(), AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure
            ? Result.Failure<SessionAnswersResponse>(recorded.Error!)
            : Result.Success(session.Value.ToAnswers(version));
    }
}
