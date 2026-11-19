using FamilyResilience.Application.Assignments;
using FamilyResilience.Domain.Assignments;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class WaveRepository(FamilyResilienceDbContext dbContext) : IWaveRepository
{
    public void Add(Wave wave) => dbContext.Waves.Add(wave);

    public Task<Wave?> GetAsync(WaveId id, CancellationToken cancellationToken = default) =>
        dbContext.Waves.SingleOrDefaultAsync(wave => wave.Id == id, cancellationToken);

    public Task<Wave?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.Waves.SingleOrDefaultAsync(wave => wave.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Wave>> ListOpenAtAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
        await dbContext.Waves
            .Where(wave => wave.Status == WaveStatus.Open && wave.OpensAt <= at && wave.ClosesAt > at)
            .OrderBy(wave => wave.OpensAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Wave>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.Waves.AsNoTracking()
            .OrderByDescending(wave => wave.OpensAt)
            .ThenByDescending(wave => wave.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        dbContext.Waves.CountAsync(cancellationToken);
}
