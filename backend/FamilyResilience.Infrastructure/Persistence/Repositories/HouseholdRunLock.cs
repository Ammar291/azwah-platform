using FamilyResilience.Application.Assignments;
using FamilyResilience.Domain.Households;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

/// <summary>Row lock on the household through the scoped context, so it joins the run's explicit transaction.</summary>
public sealed class HouseholdRunLock(FamilyResilienceDbContext dbContext) : IHouseholdRunLock
{
    public async Task LockAsync(HouseholdId householdId, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The household run lock needs an explicit transaction.");
        }

        var id = householdId.Value;
        await dbContext.Database.ExecuteSqlAsync($"SELECT id FROM households WHERE id = {id} FOR UPDATE", cancellationToken);
    }
}
