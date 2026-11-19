using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>The staff view of safety alerts, with each household's reference and the member's relationship.</summary>
public interface ISafetyAlertReadModel
{
    /// <summary>
    /// A page of alerts: open, then acknowledged, then resolved; critical before high; the latest detection first; then
    /// by ID.
    /// </summary>
    Task<PagedResponse<AdminSafetyAlertResponse>> ListAsync(SafetyAlertsQuery query, CancellationToken cancellationToken = default);

    Task<AdminSafetyAlertResponse?> GetAsync(SafetyAlertId id, CancellationToken cancellationToken = default);

    /// <summary>Counts of unresolved alerts and the latest detection among them; no personal data.</summary>
    Task<SafetyAlertSummaryResponse> SummaryAsync(CancellationToken cancellationToken = default);
}
