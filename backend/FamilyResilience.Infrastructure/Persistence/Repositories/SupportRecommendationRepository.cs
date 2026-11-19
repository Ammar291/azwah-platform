using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class SupportRecommendationRepository(FamilyResilienceDbContext dbContext) : ISupportRecommendationRepository
{
    public void Add(SupportRecommendation recommendation) => dbContext.Set<SupportRecommendation>().Add(recommendation);

    public async Task<IReadOnlyList<SupportRecommendation>> ListForSessionAsync(
        AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        await dbContext.Set<SupportRecommendation>()
            .Where(recommendation => recommendation.AssessmentSessionId == sessionId)
            .OrderBy(recommendation => recommendation.CreatedAt)
            .ThenBy(recommendation => recommendation.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupportRecommendation>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.Set<SupportRecommendation>().AsNoTracking()
            .Where(recommendation => memberIds.Contains(recommendation.FamilyMemberId))
            .OrderByDescending(recommendation => recommendation.CreatedAt)
            .ThenByDescending(recommendation => recommendation.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        dbContext.Set<SupportRecommendation>().CountAsync(recommendation => memberIds.Contains(recommendation.FamilyMemberId), cancellationToken);
}
