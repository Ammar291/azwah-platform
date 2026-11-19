using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Referrals;

/// <summary>Partner organisations by code, active and inactive, a page at a time.</summary>
public sealed class ListPartners(IPartnerOrganisationRepository partners)
{
    public async Task<Result<PagedResponse<PartnerResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await partners.ListAsync((request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await partners.CountAsync(cancellationToken);
        return Result.Success(new PagedResponse<PartnerResponse>(
            page.Select(PartnerResponse.From).ToList(), request.Page, request.PageSize, total));
    }
}
