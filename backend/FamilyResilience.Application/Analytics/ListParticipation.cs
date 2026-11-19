using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>Households and their progress, newest first, for program administrators. Every read is audited.</summary>
public sealed class ListParticipation(IMonitoringReadModel monitoring, IAuditLog auditLog)
{
    public async Task<Result<PagedResponse<ParticipationRowResponse>>> HandleAsync(ParticipationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await monitoring.ListParticipationAsync(request, cancellationToken);
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("analytics.participation.read", "participation_report", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure ? Result.Failure<PagedResponse<ParticipationRowResponse>>(recorded.Error!) : Result.Success(page);
    }
}
