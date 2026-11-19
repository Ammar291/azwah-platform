using FamilyResilience.Application.Privacy;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Privacy;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class PrivacyRequestRepository(FamilyResilienceDbContext dbContext) : IPrivacyRequestRepository
{
    public void Add(PrivacyRequest request) => dbContext.PrivacyRequests.Add(request);

    public Task<PrivacyRequest?> GetAsync(PrivacyRequestId id, CancellationToken cancellationToken = default) =>
        dbContext.PrivacyRequests.SingleOrDefaultAsync(request => request.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PrivacyRequest>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.PrivacyRequests.AsNoTracking()
            .Where(request => memberIds.Contains(request.FamilyMemberId))
            .OrderByDescending(request => request.CreatedAt)
            .ThenByDescending(request => request.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        dbContext.PrivacyRequests.CountAsync(request => memberIds.Contains(request.FamilyMemberId), cancellationToken);

    public async Task<IReadOnlyList<PrivacyRequestId>> ListOpenIdsForHouseholdAsync(
        HouseholdId householdId, CancellationToken cancellationToken = default) =>
        await dbContext.PrivacyRequests.AsNoTracking()
            .Where(request => request.HouseholdId == householdId && request.Status == PrivacyRequestStatus.Open)
            .OrderBy(request => request.CreatedAt)
            .Select(request => request.Id)
            .ToListAsync(cancellationToken);
}
