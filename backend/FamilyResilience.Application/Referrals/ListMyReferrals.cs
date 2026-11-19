using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>Referrals of the members the caller may act for, newest first, cancelled ones included.</summary>
public sealed class ListMyReferrals(ICallerAccess access, ISupportReferralRepository referrals, IPartnerOrganisationRepository partners)
{
    public async Task<Result<PagedResponse<ReferralResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var members = await access.ActableMemberIdsAsync(cancellationToken);
        if (members.Count == 0)
        {
            return Result.Success(new PagedResponse<ReferralResponse>([], request.Page, request.PageSize, 0));
        }

        var page = await referrals.ListForMembersAsync(members, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await referrals.CountForMembersAsync(members, cancellationToken);
        var partnerIds = page.Select(referral => referral.PartnerOrganisationId).OfType<PartnerOrganisationId>().Distinct().ToList();
        var handedTo = partnerIds.Count == 0
            ? new Dictionary<PartnerOrganisationId, PartnerOrganisation>()
            : (await partners.ListByIdsAsync(partnerIds, cancellationToken)).ToDictionary(partner => partner.Id);
        return Result.Success(new PagedResponse<ReferralResponse>(
            page.Select(referral => new ReferralResponse(
                referral.Id.Value,
                referral.FamilyMemberId.Value,
                referral.AssessmentSessionId.Value,
                referral.CategoryCode,
                referral.Status,
                referral.AssignedServiceCode,
                referral.ResolutionCode,
                referral.CreatedAt,
                referral.UpdatedAt,
                referral.ResolvedAt,
                HandOffResponse.Of(referral, handedTo))).ToList(),
            request.Page,
            request.PageSize,
            total));
    }
}
