using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Safety alerts for system administrators in queue order (open, acknowledged, resolved; critical first; the latest
/// detection first), with household references; every read is audited <see cref="Read"/>.
/// </summary>
public sealed class ListSafetyAlerts(ISafetyAlertReadModel alerts, IAuditLog auditLog)
{
    public const string Read = "safeguarding.alerts.read";

    public async Task<Result<PagedResponse<AdminSafetyAlertResponse>>> HandleAsync(SafetyAlertsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await alerts.ListAsync(query, cancellationToken);
        var recorded = await auditLog.RecordAsync(
            new AuditRecord(Read, "safety_alerts", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure ? Result.Failure<PagedResponse<AdminSafetyAlertResponse>>(recorded.Error!) : Result.Success(page);
    }
}
