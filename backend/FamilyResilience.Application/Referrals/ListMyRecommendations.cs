using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>Recommendations for the members the caller may act for, newest first, with their referral's status.</summary>
public sealed class ListMyRecommendations(
    ICallerAccess access, ISupportRecommendationRepository recommendations, ISupportReferralRepository referrals)
{
    public async Task<Result<PagedResponse<RecommendationResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var members = await access.ActableMemberIdsAsync(cancellationToken);
        if (members.Count == 0)
        {
            return Result.Success(new PagedResponse<RecommendationResponse>([], request.Page, request.PageSize, 0));
        }

        var page = await recommendations.ListForMembersAsync(members, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await recommendations.CountForMembersAsync(members, cancellationToken);
        var referralIds = page.Select(recommendation => recommendation.SupportReferralId).OfType<SupportReferralId>().ToList();
        var statuses = (await referrals.ListByIdsAsync(referralIds, cancellationToken)).ToDictionary(referral => referral.Id, referral => referral.Status);
        return Result.Success(new PagedResponse<RecommendationResponse>(
            page.Select(recommendation => new RecommendationResponse(
                recommendation.Id.Value,
                recommendation.FamilyMemberId.Value,
                recommendation.AssessmentSessionId.Value,
                recommendation.CategoryCode,
                recommendation.CreatedAt,
                recommendation.SupportReferralId?.Value,
                recommendation.SupportReferralId is { } referralId ? statuses[referralId] : null)).ToList(),
            request.Page,
            request.PageSize,
            total));
    }
}
