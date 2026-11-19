using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Counts of unresolved alerts and the latest detection among them, for the dashboard's badge and new-alert toast. It
/// holds no personal data and is polled every 30 seconds, so it is not audited.
/// </summary>
public sealed class GetSafetyAlertSummary(ISafetyAlertReadModel alerts)
{
    public async Task<Result<SafetyAlertSummaryResponse>> HandleAsync(CancellationToken cancellationToken) =>
        Result.Success(await alerts.SummaryAsync(cancellationToken));
}
