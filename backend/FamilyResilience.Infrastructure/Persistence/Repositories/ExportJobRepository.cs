using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Analytics;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class ExportJobRepository(FamilyResilienceDbContext dbContext) : IExportJobRepository
{
    public void Add(ExportJob job) => dbContext.ExportJobs.Add(job);

    public Task<ExportJob?> GetAsync(ExportJobId id, CancellationToken cancellationToken = default) =>
        dbContext.ExportJobs.SingleOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ExportJob>> ListRequestedByAsync(
        string subject, int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.ExportJobs.AsNoTracking()
            .Where(job => job.RequestedBy == subject)
            .OrderByDescending(job => job.RequestedAt)
            .ThenByDescending(job => job.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountRequestedByAsync(string subject, CancellationToken cancellationToken = default) =>
        dbContext.ExportJobs.CountAsync(job => job.RequestedBy == subject, cancellationToken);

    public Task<int> CountActiveRequestedByAsync(string subject, CancellationToken cancellationToken = default) =>
        dbContext.ExportJobs.CountAsync(
            job => job.RequestedBy == subject && (job.Status == ExportStatus.Queued || job.Status == ExportStatus.Running),
            cancellationToken);

    public Task<int> CountRequestedSinceAsync(string subject, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        dbContext.ExportJobs.CountAsync(job => job.RequestedBy == subject && job.RequestedAt >= since, cancellationToken);

    public Task<int> DeleteFinishedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default) =>
        dbContext.ExportJobs
            .Where(job => (job.Status == ExportStatus.Failed || job.Status == ExportStatus.Expired) && job.UpdatedAt < before)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<ExportJob>> ListExpiredAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
        await dbContext.ExportJobs
            .Where(job => job.Status == ExportStatus.Completed && job.ExpiresAt <= now)
            .OrderBy(job => job.ExpiresAt)
            .ThenBy(job => job.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExportJob>> ListStalledAsync(DateTimeOffset before, int take, CancellationToken cancellationToken = default) =>
        await dbContext.ExportJobs
            .Where(job => (job.Status == ExportStatus.Queued || job.Status == ExportStatus.Running) && job.UpdatedAt < before)
            .OrderBy(job => job.UpdatedAt)
            .ThenBy(job => job.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
}
