using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Sessions;

public sealed class GetSession(IAssessmentSessionRepository sessions, IInstrumentVersionRepository versions, IParticipantAccess access)
{
    public async Task<Result<SessionResponse>> HandleAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (session.IsFailure)
        {
            return Result.Failure<SessionResponse>(session.Error!);
        }

        var version = await versions.GetAsync(session.Value.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        return Result.Success(session.Value.ToResponse(version));
    }
}
