using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Scoring;

/// <summary>Every result recorded for a session, oldest first. Reading scores is audited.</summary>
public sealed class ListScoreResults(IAssessmentSessionRepository sessions, IScoreResultRepository scoreResults, IAuditLog auditLog)
{
    public async Task<Result<IReadOnlyList<ScoreResultResponse>>> HandleAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var id = AssessmentSessionId.From(sessionId);
        if (await sessions.GetAsync(id, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<ScoreResultResponse>>(SessionErrors.SessionNotFound);
        }

        var results = await scoreResults.ListForSessionAsync(id, cancellationToken);
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("scoring.score_results.read", "assessment_session", sessionId.ToString(), AuditOutcome.Succeeded),
            cancellationToken);
        return recorded.IsFailure
            ? Result.Failure<IReadOnlyList<ScoreResultResponse>>(recorded.Error!)
            : Result.Success<IReadOnlyList<ScoreResultResponse>>(results.Select(result => result.ToResponse()).ToList());
    }
}
