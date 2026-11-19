using FamilyResilience.Application.Privacy;
using FamilyResilience.Domain.Privacy;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>Untracked reads of privacy requests for staff: open ones first, then oldest first, with the household reference.</summary>
public sealed class PrivacyRequestReadModel(FamilyResilienceDbContext dbContext) : IPrivacyRequestReadModel
{
    public async Task<IReadOnlyList<AdminPrivacyRequestResponse>> ListAsync(
        PrivacyRequestStatus? status, int skip, int take, CancellationToken cancellationToken = default) =>
        await Project(Filtered(status)
                .OrderBy(row => row.Request.Status == PrivacyRequestStatus.Open ? 0 : 1)
                .ThenBy(row => row.Request.CreatedAt)
                .ThenBy(row => row.Request.Id)
                .Skip(skip)
                .Take(take))
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(PrivacyRequestStatus? status, CancellationToken cancellationToken = default) =>
        Filtered(status).CountAsync(cancellationToken);

    public Task<AdminPrivacyRequestResponse?> GetAsync(PrivacyRequestId id, CancellationToken cancellationToken = default) =>
        Project(Rows().Where(row => row.Request.Id == id)).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<RequestRow> Filtered(PrivacyRequestStatus? status) =>
        status is { } wanted ? Rows().Where(row => row.Request.Status == wanted) : Rows();

    private IQueryable<RequestRow> Rows() =>
        from request in dbContext.PrivacyRequests.AsNoTracking()
        join household in dbContext.Households on request.HouseholdId equals household.Id
        select new RequestRow { Request = request, HouseholdReference = household.Reference.Value };

    private static IQueryable<AdminPrivacyRequestResponse> Project(IQueryable<RequestRow> rows) =>
        rows.Select(row => new AdminPrivacyRequestResponse(
            row.Request.Id.Value,
            row.HouseholdReference,
            row.Request.FamilyMemberId.Value,
            row.Request.Type,
            row.Request.Status,
            row.Request.CreatedAt,
            row.Request.ClosedAt,
            row.Request.Resolution));

    private sealed class RequestRow
    {
        public required PrivacyRequest Request { get; init; }

        public required string HouseholdReference { get; init; }
    }
}
