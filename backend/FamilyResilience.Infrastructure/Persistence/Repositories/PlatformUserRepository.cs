using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class PlatformUserRepository(FamilyResilienceDbContext dbContext) : IPlatformUserRepository
{
    public void Add(PlatformUser user) => dbContext.PlatformUsers.Add(user);

    public Task<PlatformUser?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default) =>
        dbContext.PlatformUsers.SingleOrDefaultAsync(user => user.Subject == subject, cancellationToken);

    public Task<DateTimeOffset?> FindRegisteredAtAsync(string subject, CancellationToken cancellationToken = default) =>
        dbContext.PlatformUsers.AsNoTracking()
            .Where(user => user.Subject == subject)
            .Select(user => (DateTimeOffset?)user.CreatedAt)
            .SingleOrDefaultAsync(cancellationToken);
}
