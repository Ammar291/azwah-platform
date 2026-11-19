using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>
/// Where a referral was handed and how. <c>method</c> is always <c>manual</c>: a support coordinator passed it on outside
/// the platform, and the platform sent the partner nothing.
/// </summary>
public sealed record HandOffResponse(string PartnerCode, string PartnerNameAr, string PartnerNameEn, HandOffMethod Method, DateTimeOffset HandedOffAt)
{
    public static HandOffResponse? Of(SupportReferral referral, IReadOnlyDictionary<PartnerOrganisationId, PartnerOrganisation> partners)
    {
        ArgumentNullException.ThrowIfNull(referral);
        ArgumentNullException.ThrowIfNull(partners);

        if (referral.PartnerOrganisationId is not { } partnerId)
        {
            return null;
        }

        var partner = partners[partnerId];
        return new HandOffResponse(
            partner.Code,
            partner.Name.Arabic ?? throw new InvalidOperationException("A partner's name has Arabic."),
            partner.Name.English,
            referral.HandOffMethod ?? throw new InvalidOperationException("A handed-off referral records its method."),
            referral.HandedOffAt ?? throw new InvalidOperationException("A handed-off referral records its time."));
    }
}
