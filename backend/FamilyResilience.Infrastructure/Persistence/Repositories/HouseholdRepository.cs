using FamilyResilience.Application.Households;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class HouseholdRepository(FamilyResilienceDbContext dbContext) : IHouseholdRepository
{
    public void Add(Household household) => dbContext.Households.Add(household);

    public async Task<IReadOnlyList<WithdrawnHousehold>> ListWithdrawnBeforeAsync(
        DateTimeOffset before, int take, CancellationToken cancellationToken = default) =>
        (await dbContext.Households.AsNoTracking()
            .Where(household => household.Status == HouseholdStatus.Withdrawn && household.UpdatedAt < before)
            .OrderBy(household => household.UpdatedAt)
            .Take(take)
            .Select(household => new { household.Id, household.Reference })
            .ToListAsync(cancellationToken))
        .Select(household => new WithdrawnHousehold(household.Id.Value, household.Reference.Value))
        .ToList();

    public Task<Household?> GetAsync(HouseholdId id, CancellationToken cancellationToken = default) =>
        dbContext.Households
            .Include(household => household.Members)
            .Include(household => household.Memberships)
            .AsSplitQuery()
            .SingleOrDefaultAsync(household => household.Id == id, cancellationToken);

    public Task<bool> ReferenceExistsAsync(HouseholdReference reference, CancellationToken cancellationToken = default) =>
        dbContext.Households.AnyAsync(household => household.Reference == reference, cancellationToken);

    public async Task<HouseholdStatus?> StatusOfMemberHouseholdAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default)
    {
        var statuses = await dbContext.Households.AsNoTracking()
            .Where(household => household.Members.Any(member => member.Id == memberId))
            .Select(household => household.Status)
            .ToListAsync(cancellationToken);
        return statuses.Count == 0 ? null : statuses[0];
    }
}
