using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>Assignments and their progress, newest first, for program administrators. Every read is audited.</summary>
public sealed class ListAssessments(IMonitoringReadModel monitoring, IAuditLog auditLog)
{
    public async Task<Result<PagedResponse<AssessmentRowResponse>>> HandleAsync(AssessmentMonitoringRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await monitoring.ListAssessmentsAsync(request, cancellationToken);
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("analytics.assessments.read", "assessment_report", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure ? Result.Failure<PagedResponse<AssessmentRowResponse>>(recorded.Error!) : Result.Success(page);
    }
}
