using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Privacy;

/// <summary>Privacy requests for program administrators, oldest first, with household references; every read is audited.</summary>
public sealed class ListPrivacyRequests(IPrivacyRequestReadModel requests, IAuditLog auditLog)
{
    public async Task<Result<PagedResponse<AdminPrivacyRequestResponse>>> HandleAsync(PrivacyRequestsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = new PagedResponse<AdminPrivacyRequestResponse>(
            await requests.ListAsync(query.Status, (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken),
            query.Page,
            query.PageSize,
            await requests.CountAsync(query.Status, cancellationToken));
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("privacy.requests.read", "privacy_requests", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure ? Result.Failure<PagedResponse<AdminPrivacyRequestResponse>>(recorded.Error!) : Result.Success(page);
    }
}
