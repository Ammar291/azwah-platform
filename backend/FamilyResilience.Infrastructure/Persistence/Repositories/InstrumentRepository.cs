using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class InstrumentRepository(FamilyResilienceDbContext dbContext) : IInstrumentRepository
{
    public void Add(Instrument instrument) => dbContext.Instruments.Add(instrument);

    public Task<Instrument?> GetAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
        dbContext.Instruments.SingleOrDefaultAsync(instrument => instrument.Id == id, cancellationToken);

    public Task<Instrument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.Instruments.SingleOrDefaultAsync(instrument => instrument.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Instrument>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.Instruments.OrderBy(instrument => instrument.Code).Skip(skip).Take(take).ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => dbContext.Instruments.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Instrument>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Instruments
            .Where(instrument => instrument.Status == InstrumentStatus.Active)
            .OrderBy(instrument => instrument.Code)
            .ToListAsync(cancellationToken);
}
