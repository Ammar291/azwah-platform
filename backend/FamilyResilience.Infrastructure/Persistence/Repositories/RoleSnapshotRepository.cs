using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class RoleSnapshotRepository(FamilyResilienceDbContext dbContext) : IRoleSnapshotRepository
{
    public void Add(RoleSnapshot snapshot) => dbContext.RoleSnapshots.Add(snapshot);

    public Task<RoleSnapshot?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default) =>
        dbContext.RoleSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Subject == subject, cancellationToken);
}
