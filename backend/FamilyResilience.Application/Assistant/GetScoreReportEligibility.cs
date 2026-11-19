using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Whether a report can be written ahead for this session. Sends and reveals nothing: the same checks as writing, and any
/// refusal but "not found" is simply no.
/// </summary>
public sealed class GetScoreReportEligibility(ScoreReportPreparation preparation)
{
    public async Task<Result<ScoreReportEligibilityResponse>> HandleAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var prepared = await preparation.LoadAsync(sessionId, cancellationToken);
        return prepared.IsFailure && prepared.Error == SessionErrors.SessionNotFound
            ? Result.Failure<ScoreReportEligibilityResponse>(prepared.Error)
            : Result.Success(new ScoreReportEligibilityResponse(prepared.IsSuccess));
    }
}
