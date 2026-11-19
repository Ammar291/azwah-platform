using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class SupportReferralRepository(FamilyResilienceDbContext dbContext) : ISupportReferralRepository
{
    public void Add(SupportReferral referral) => dbContext.SupportReferrals.Add(referral);

    public Task<SupportReferral?> GetAsync(SupportReferralId id, CancellationToken cancellationToken = default) =>
        dbContext.SupportReferrals.SingleOrDefaultAsync(referral => referral.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SupportReferral>> ListForSessionAsync(
        AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        await dbContext.SupportReferrals
            .Where(referral => referral.AssessmentSessionId == sessionId)
            .OrderBy(referral => referral.CreatedAt)
            .ThenBy(referral => referral.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupportReferral>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.SupportReferrals.AsNoTracking()
            .Where(referral => memberIds.Contains(referral.FamilyMemberId))
            .OrderByDescending(referral => referral.CreatedAt)
            .ThenByDescending(referral => referral.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        dbContext.SupportReferrals.CountAsync(referral => memberIds.Contains(referral.FamilyMemberId), cancellationToken);

    public async Task<IReadOnlyList<SupportReferral>> ListByIdsAsync(
        IReadOnlyCollection<SupportReferralId> ids, CancellationToken cancellationToken = default) =>
        await dbContext.SupportReferrals.AsNoTracking().Where(referral => ids.Contains(referral.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupportReferral>> ListLiveForConsentAsync(
        ConsentRecordId consentId, CancellationToken cancellationToken = default) =>
        await dbContext.SupportReferrals
            .Where(referral => referral.ConsentRecordId == consentId
                && (referral.Status == ReferralStatus.Open || referral.Status == ReferralStatus.InProgress))
            .OrderBy(referral => referral.CreatedAt)
            .ThenBy(referral => referral.Id)
            .ToListAsync(cancellationToken);
}
