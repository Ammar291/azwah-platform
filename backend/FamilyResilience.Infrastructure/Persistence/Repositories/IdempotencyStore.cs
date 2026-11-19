using FamilyResilience.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyStore(FamilyResilienceDbContext dbContext) : IIdempotencyStore
{
    public Task<IdempotencyRecord?> FindAsync(string subject, Guid key, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        dbContext.Set<IdempotencyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(record => record.Subject == subject && record.Key == key && record.ExpiresAt > now, cancellationToken);

    public void Add(IdempotencyRecord record) => dbContext.Set<IdempotencyRecord>().Add(record);

    public Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        dbContext.Set<IdempotencyRecord>().Where(record => record.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
}
