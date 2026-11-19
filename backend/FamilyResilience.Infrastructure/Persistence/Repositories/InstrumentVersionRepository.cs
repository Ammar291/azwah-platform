using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class InstrumentVersionRepository(FamilyResilienceDbContext dbContext) : IInstrumentVersionRepository
{
    public void Add(InstrumentVersion version) => dbContext.InstrumentVersions.Add(version);

    public Task<InstrumentVersion?> GetAsync(InstrumentVersionId id, CancellationToken cancellationToken = default) =>
        WithContent().SingleOrDefaultAsync(version => version.Id == id, cancellationToken);

    public Task<InstrumentVersion?> GetEffectiveApprovedAsync(
        InstrumentId instrumentId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        WithContent().SingleOrDefaultAsync(
            version => version.InstrumentId == instrumentId
                && version.Status == InstrumentVersionStatus.Approved
                && version.EffectiveFrom <= at
                && (version.EffectiveTo == null || version.EffectiveTo > at),
            cancellationToken);

    public Task<InstrumentVersion?> GetByNumberAsync(
        InstrumentId instrumentId, int versionNumber, CancellationToken cancellationToken = default) =>
        WithContent().SingleOrDefaultAsync(
            version => version.InstrumentId == instrumentId && version.VersionNumber == versionNumber, cancellationToken);

    public async Task<IReadOnlyList<InstrumentVersionSummaryResponse>> ListSummariesAsync(
        InstrumentId instrumentId, int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.InstrumentVersions.AsNoTracking()
            .Where(version => version.InstrumentId == instrumentId)
            .OrderByDescending(version => version.VersionNumber)
            .Skip(skip)
            .Take(take)
            .Select(version => new InstrumentVersionSummaryResponse(
                version.Id.Value,
                version.InstrumentId.Value,
                version.VersionNumber,
                version.Status,
                version.ApprovedBy,
                version.ApprovedAt,
                version.ApprovalSource,
                version.EffectiveFrom,
                version.EffectiveTo,
                version.RetiredAt,
                version.Sections.Count,
                version.Sections.Sum(section => section.Items.Count)))
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default) =>
        dbContext.InstrumentVersions.CountAsync(version => version.InstrumentId == instrumentId, cancellationToken);

    public Task<bool> ExistsAsync(InstrumentVersionId id, CancellationToken cancellationToken = default) =>
        dbContext.InstrumentVersions.AnyAsync(version => version.Id == id, cancellationToken);

    public async Task<int> GetHighestVersionNumberAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default) =>
        await dbContext.InstrumentVersions
            .Where(version => version.InstrumentId == instrumentId)
            .MaxAsync(version => (int?)version.VersionNumber, cancellationToken) ?? 0;

    // Split queries avoid multiplying every option by every section in one result set.
    private IQueryable<InstrumentVersion> WithContent() =>
        dbContext.InstrumentVersions
            .Include(version => version.Sections)
            .ThenInclude(section => section.Items)
            .ThenInclude(item => item.Options)
            .AsSplitQuery();
}
