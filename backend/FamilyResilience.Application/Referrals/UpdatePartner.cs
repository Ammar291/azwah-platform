using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>Activates or deactivates a partner organisation. Hand-offs already made stay as they are.</summary>
public sealed class UpdatePartner(
    IPartnerOrganisationRepository partners, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<PartnerResponse>> HandleAsync(Guid partnerId, UpdatePartnerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var partner = await partners.GetAsync(PartnerOrganisationId.From(partnerId), cancellationToken);
        if (partner is null)
        {
            return Result.Failure<PartnerResponse>(ReferralErrors.PartnerNotFound);
        }

        var changed = partner.SetActive(request.IsActive ?? throw new ArgumentException("IsActive is required.", nameof(request)), timeProvider.GetUtcNow());
        if (changed.IsFailure)
        {
            return Result.Failure<PartnerResponse>(changed.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "referrals.partner.updated", "partner_organisation", partnerId, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<PartnerResponse>(committed.Error!)
            : Result.Success(PartnerResponse.From(partner));
    }
}
