using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Referrals;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class PartnerOrganisationRepository(FamilyResilienceDbContext dbContext) : IPartnerOrganisationRepository
{
    public void Add(PartnerOrganisation partner) => dbContext.PartnerOrganisations.Add(partner);

    public Task<PartnerOrganisation?> GetAsync(PartnerOrganisationId id, CancellationToken cancellationToken = default) =>
        dbContext.PartnerOrganisations.SingleOrDefaultAsync(partner => partner.Id == id, cancellationToken);

    public async Task<PartnerOrganisation?> GetWithShareLockAsync(PartnerOrganisationId id, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A share lock needs an explicit transaction: call this inside IUnitOfWork.ExecuteInTransactionAsync.");
        }

        var value = id.Value;
        await dbContext.Database.ExecuteSqlAsync($"SELECT id FROM partner_organisations WHERE id = {value} FOR SHARE", cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<PartnerOrganisation>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.PartnerOrganisations.AsNoTracking()
            .OrderBy(partner => partner.Code)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        dbContext.PartnerOrganisations.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<PartnerOrganisation>> ListByIdsAsync(
        IReadOnlyCollection<PartnerOrganisationId> ids, CancellationToken cancellationToken = default) =>
        await dbContext.PartnerOrganisations.AsNoTracking().Where(partner => ids.Contains(partner.Id)).ToListAsync(cancellationToken);
}
