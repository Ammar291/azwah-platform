using System.Text.Json;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Safeguarding;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Untracked reads of safety alerts for staff: open, then acknowledged, then resolved; critical before high; the latest
/// detection first. Status and severity are ranked explicitly, because their stored names do not sort that way. The
/// household reference and the member's relationship come from left joins, since either may be absent.
/// </summary>
public sealed class SafetyAlertReadModel(FamilyResilienceDbContext dbContext) : ISafetyAlertReadModel
{
    public async Task<PagedResponse<AdminSafetyAlertResponse>> ListAsync(SafetyAlertsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        IPageRequest page = query;
        var alerts = Filtered(query);
        var total = await alerts.CountAsync(cancellationToken);
        var rows = await Rows(alerts)
            .OrderBy(row => row.Alert.Status == SafetyAlertStatus.Open ? 0 : row.Alert.Status == SafetyAlertStatus.Acknowledged ? 1 : 2)
            .ThenBy(row => row.Alert.Severity == SafetySeverity.Critical ? 0 : 1)
            .ThenByDescending(row => row.Alert.LastDetectedAt)
            .ThenBy(row => row.Alert.Id)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResponse<AdminSafetyAlertResponse>([.. rows.Select(ToResponse)], page.Page, page.PageSize, total);
    }

    public async Task<AdminSafetyAlertResponse?> GetAsync(SafetyAlertId id, CancellationToken cancellationToken = default)
    {
        var row = await Rows(dbContext.SafetyAlerts.AsNoTracking().Where(alert => alert.Id == id)).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToResponse(row);
    }

    public async Task<SafetyAlertSummaryResponse> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var summary = await dbContext.SafetyAlerts.AsNoTracking()
            .Where(alert => alert.Status != SafetyAlertStatus.Resolved)
            .GroupBy(_ => 1)
            .Select(unresolved => new SafetyAlertSummaryResponse(
                unresolved.Count(alert => alert.Status == SafetyAlertStatus.Open),
                unresolved.Count(alert => alert.Status == SafetyAlertStatus.Acknowledged),
                unresolved.Count(alert => alert.Status == SafetyAlertStatus.Open && alert.Severity == SafetySeverity.Critical),
                unresolved.Max(alert => (DateTimeOffset?)alert.LastDetectedAt)))
            .SingleOrDefaultAsync(cancellationToken);
        return summary ?? new SafetyAlertSummaryResponse(0, 0, 0, null);
    }

    private IQueryable<SafetyAlert> Filtered(SafetyAlertsQuery query)
    {
        var alerts = dbContext.SafetyAlerts.AsNoTracking();
        alerts = query.Status switch
        {
            SafetyAlertStatusFilter.Open => alerts.Where(alert => alert.Status == SafetyAlertStatus.Open),
            SafetyAlertStatusFilter.Acknowledged => alerts.Where(alert => alert.Status == SafetyAlertStatus.Acknowledged),
            SafetyAlertStatusFilter.Resolved => alerts.Where(alert => alert.Status == SafetyAlertStatus.Resolved),
            SafetyAlertStatusFilter.Unresolved => alerts.Where(alert => alert.Status != SafetyAlertStatus.Resolved),
            _ => alerts,
        };
        return query.Category is { } category ? alerts.Where(alert => alert.Concern == category) : alerts;
    }

    private IQueryable<AlertRow> Rows(IQueryable<SafetyAlert> alerts) =>
        alerts
            .LeftJoin(
                dbContext.Households,
                alert => alert.HouseholdId,
                household => (HouseholdId?)household.Id,
                (alert, household) => new { Alert = alert, Reference = household == null ? null : household.Reference })
            .LeftJoin(
                dbContext.Set<FamilyMember>(),
                row => row.Alert.MemberId,
                member => (FamilyMemberId?)member.Id,
                (row, member) => new AlertRow
                {
                    Alert = row.Alert,
                    HouseholdReference = row.Reference,
                    IsHeadOfHousehold = member == null ? null : member.IsHeadOfHousehold,
                    Relationship = member == null ? null : member.RelationshipToHousehold,
                });

    private static AdminSafetyAlertResponse ToResponse(AlertRow row) => new(
        row.Alert.Id.Value,
        row.HouseholdReference?.Value,
        MemberRelationship(row.IsHeadOfHousehold, row.Relationship),
        row.Alert.Source,
        row.Alert.Concern,
        row.Alert.Severity,
        row.Alert.Detector,
        row.Alert.SignalCode,
        row.Alert.Language,
        row.Alert.OccurrenceCount,
        row.Alert.FirstDetectedAt,
        row.Alert.LastDetectedAt,
        row.Alert.Status,
        row.Alert.AcknowledgedAt,
        row.Alert.ResolvedAt,
        row.Alert.Resolution);

    /// <summary>The relationship code as the roster writes it on the wire; the head has none, so it is named.</summary>
    private static string? MemberRelationship(bool? isHeadOfHousehold, HouseholdRelationship? relationship) =>
        isHeadOfHousehold == true
            ? AdminSafetyAlertResponse.HeadOfHousehold
            : relationship is { } value ? JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()) : null;

    private sealed class AlertRow
    {
        public required SafetyAlert Alert { get; init; }

        public required HouseholdReference? HouseholdReference { get; init; }

        public required bool? IsHeadOfHousehold { get; init; }

        public required HouseholdRelationship? Relationship { get; init; }
    }
}
