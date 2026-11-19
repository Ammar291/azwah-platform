using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

public sealed record PartnerResponse(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    IReadOnlyList<string> Categories,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PartnerResponse From(PartnerOrganisation partner)
    {
        ArgumentNullException.ThrowIfNull(partner);
        return new PartnerResponse(
            partner.Id.Value,
            partner.Code,
            partner.Name.Arabic ?? throw new InvalidOperationException("A partner's name has Arabic."),
            partner.Name.English,
            partner.Categories,
            partner.IsActive,
            partner.CreatedAt,
            partner.UpdatedAt);
    }
}
