using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

public sealed class GetPartner(IPartnerOrganisationRepository partners)
{
    public async Task<Result<PartnerResponse>> HandleAsync(Guid partnerId, CancellationToken cancellationToken) =>
        await partners.GetAsync(PartnerOrganisationId.From(partnerId), cancellationToken) is { } partner
            ? Result.Success(PartnerResponse.From(partner))
            : Result.Failure<PartnerResponse>(ReferralErrors.PartnerNotFound);
}
