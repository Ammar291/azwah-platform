using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Scoring;

/// <summary>Households' latest results, newest first, for system administrators (D-104). Every read is audited.</summary>
public sealed class ListHouseholdResults(IHouseholdResultReadModel results, IAuditLog auditLog)
{
    public async Task<Result<PagedResponse<HouseholdResultRowResponse>>> HandleAsync(HouseholdResultsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await results.ListLatestAsync(request, cancellationToken);
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("scoring.household_results.read", "score_report", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure ? Result.Failure<PagedResponse<HouseholdResultRowResponse>>(recorded.Error!) : Result.Success(page);
    }
}
