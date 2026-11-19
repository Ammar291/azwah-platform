using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class SupportResourceRepository(FamilyResilienceDbContext dbContext) : ISupportResourceRepository
{
    public void Add(SupportResource resource) => dbContext.SupportResources.Add(resource);

    public Task<SupportResource?> GetAsync(SupportResourceId id, CancellationToken cancellationToken = default) =>
        dbContext.SupportResources.SingleOrDefaultAsync(resource => resource.Id == id, cancellationToken);

    public Task<SupportResource?> GetApprovedAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.SupportResources.SingleOrDefaultAsync(
            resource => resource.Code == code && resource.Status == SupportResourceStatus.Approved, cancellationToken);

    public async Task<int> NextVersionNumberAsync(string code, CancellationToken cancellationToken = default)
    {
        var highest = await dbContext.SupportResources
            .Where(resource => resource.Code == code)
            .MaxAsync(resource => (int?)resource.VersionNumber, cancellationToken);
        return (highest ?? 0) + 1;
    }

    public async Task<IReadOnlyList<SupportResource>> ListAsync(
        SupportResourceStatus? status, int skip, int take, CancellationToken cancellationToken = default) =>
        await Filter(status).AsNoTracking()
            .OrderBy(resource => resource.Code)
            .ThenByDescending(resource => resource.VersionNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(SupportResourceStatus? status, CancellationToken cancellationToken = default) =>
        Filter(status).CountAsync(cancellationToken);

    private IQueryable<SupportResource> Filter(SupportResourceStatus? status) =>
        status is { } value
            ? dbContext.SupportResources.Where(resource => resource.Status == value)
            : dbContext.SupportResources;
}
