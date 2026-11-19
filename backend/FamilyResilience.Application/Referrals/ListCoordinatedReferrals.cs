using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Referrals;

/// <summary>The referral queue for support coordinators, newest first, optionally by status. Every read is audited.</summary>
public sealed class ListCoordinatedReferrals(IReferralCoordinationReadModel referrals, IAuditLog auditLog)
{
    public async Task<Result<PagedResponse<CoordinatedReferralResponse>>> HandleAsync(
        CoordinatedReferralsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await referrals.ListAsync(request.Status, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await referrals.CountAsync(request.Status, cancellationToken);
        var recorded = await auditLog.RecordAsync(
            new AuditRecord("referrals.referrals.read", "referral_queue", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure
            ? Result.Failure<PagedResponse<CoordinatedReferralResponse>>(recorded.Error!)
            : Result.Success(new PagedResponse<CoordinatedReferralResponse>(page, request.Page, request.PageSize, total));
    }
}
