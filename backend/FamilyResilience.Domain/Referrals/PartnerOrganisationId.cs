using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Referrals;

public readonly record struct PartnerOrganisationId(Guid Value) : IEntityId<PartnerOrganisationId>
{
    public static PartnerOrganisationId New() => new(Guid.CreateVersion7());

    public static PartnerOrganisationId From(Guid value) => new(value);
}
