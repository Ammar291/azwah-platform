using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>The exports the signed-in researcher requested, newest first. Nobody sees another researcher's exports.</summary>
public sealed class ListExports(ICurrentUser currentUser, IExportJobRepository jobs, TimeProvider timeProvider)
{
    public async Task<Result<PagedResponse<ExportResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.SubjectId is not { } subject)
        {
            return Result.Success(new PagedResponse<ExportResponse>([], request.Page, request.PageSize, 0));
        }

        var page = await jobs.ListRequestedByAsync(subject, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await jobs.CountRequestedByAsync(subject, cancellationToken);
        var now = timeProvider.GetUtcNow();
        return Result.Success(new PagedResponse<ExportResponse>(
            page.Select(job => job.ToResponse(now)).ToList(), request.Page, request.PageSize, total));
    }
}
