using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Tier distributions of complete scores for researchers (D-59, D-62). Not part of the overview: program administrators
/// see which household was scored and when, so comparing two reads would reveal that household's tier. The database
/// counts; small cells are suppressed. Every read is audited.
/// </summary>
public sealed class GetScoreDistributions(IAnalyticsQueries queries, AnalyticsSettings settings, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<ScoreDistributionsResponse>> HandleAsync(ScoreDistributionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tiers = await queries.TierCountsAsync(request.WaveId is { } waveId ? WaveId.From(waveId) : null, cancellationToken);
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("analytics.score_distributions.read", "score_distributions", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure
            ? Result.Failure<ScoreDistributionsResponse>(recorded.Error!)
            : Result.Success(new ScoreDistributionsResponse(
                timeProvider.GetUtcNow(), settings.MinimumGroupSize, request.WaveId, ScoreDistributions.Build(tiers, settings.MinimumGroupSize)));
    }
}
