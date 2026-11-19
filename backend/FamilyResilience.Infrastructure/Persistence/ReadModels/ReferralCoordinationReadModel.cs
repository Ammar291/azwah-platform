using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Referrals;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Untracked reads of the referral queue: each referral with its partner, if any, and its household reference while
/// the referral's consent is still active.
/// </summary>
public sealed class ReferralCoordinationReadModel(FamilyResilienceDbContext dbContext) : IReferralCoordinationReadModel
{
    public async Task<IReadOnlyList<CoordinatedReferralResponse>> ListAsync(
        ReferralStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var rows = await Filtered(status)
            .OrderByDescending(row => row.Referral.CreatedAt)
            .ThenByDescending(row => row.Referral.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return await ToResponsesAsync(rows, cancellationToken);
    }

    public Task<int> CountAsync(ReferralStatus? status, CancellationToken cancellationToken = default) =>
        Filtered(status).CountAsync(cancellationToken);

    public async Task<CoordinatedReferralResponse?> GetAsync(SupportReferralId id, CancellationToken cancellationToken = default)
    {
        var rows = await Rows().Where(row => row.Referral.Id == id).ToListAsync(cancellationToken);
        return (await ToResponsesAsync(rows, cancellationToken)).SingleOrDefault();
    }

    private IQueryable<ReferralRow> Filtered(ReferralStatus? status) =>
        status is { } wanted ? Rows().Where(row => row.Referral.Status == wanted) : Rows();

    private IQueryable<ReferralRow> Rows() =>
        from referral in dbContext.SupportReferrals.AsNoTracking()
        join member in dbContext.Set<FamilyMember>() on referral.FamilyMemberId equals member.Id
        join household in dbContext.Households on member.HouseholdId equals household.Id
        join consent in dbContext.ConsentRecords on referral.ConsentRecordId equals consent.Id
        select new ReferralRow
        {
            Referral = referral, HouseholdReference = household.Reference, ConsentActive = consent.WithdrawnAt == null,
        };

    private async Task<IReadOnlyList<CoordinatedReferralResponse>> ToResponsesAsync(
        IReadOnlyList<ReferralRow> rows, CancellationToken cancellationToken)
    {
        var partnerIds = rows.Select(row => row.Referral.PartnerOrganisationId).OfType<PartnerOrganisationId>().Distinct().ToList();
        var partners = partnerIds.Count == 0
            ? new Dictionary<PartnerOrganisationId, PartnerOrganisation>()
            : await dbContext.PartnerOrganisations.AsNoTracking()
                .Where(partner => partnerIds.Contains(partner.Id))
                .ToDictionaryAsync(partner => partner.Id, cancellationToken);
        return rows
            .Select(row => new CoordinatedReferralResponse(
                row.Referral.Id.Value,
                row.ConsentActive ? row.HouseholdReference.Value : null,
                row.Referral.CategoryCode,
                row.Referral.Status,
                row.Referral.CreatedAt,
                row.Referral.UpdatedAt,
                HandOffResponse.Of(row.Referral, partners)))
            .ToList();
    }

    /// <summary>Member-initialised, so EF Core can still filter and order by its parts.</summary>
    private sealed class ReferralRow
    {
        public required SupportReferral Referral { get; init; }

        public required HouseholdReference HouseholdReference { get; init; }

        public required bool ConsentActive { get; init; }
    }
}
