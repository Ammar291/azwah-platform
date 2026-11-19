using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// The approved directory as the caller may see it, ordered by code, a page at a time: the same filters as the search
/// (approved, before the review date, visible to the caller's roles). Nothing about the caller is used but their roles,
/// and no read is audited, like the search.
/// </summary>
public sealed class ListPublishedSupportResources(ISupportResourceSearch search, ICurrentUser currentUser, TimeProvider timeProvider)
{
    public async Task<Result<PagedResponse<PublishedSupportResourceResponse>>> HandleAsync(
        PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var visibilities = SupportResourceAccess.VisibilitiesFor(currentUser.Roles);
        var now = timeProvider.GetUtcNow();
        var page = await search.ListAsync(visibilities, now, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await search.CountAsync(visibilities, now, cancellationToken);
        return Result.Success(new PagedResponse<PublishedSupportResourceResponse>(
            page.Select(PublishedSupportResourceResponse.From).ToList(), request.Page, request.PageSize, total));
    }
}
