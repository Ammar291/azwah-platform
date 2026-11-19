using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.SupportResources;

/// <summary>Every version in the directory, by code then newest version, a page at a time.</summary>
public sealed class ListSupportResources(ISupportResourceRepository resources, TimeProvider timeProvider)
{
    public async Task<Result<PagedResponse<SupportResourceResponse>>> HandleAsync(
        ListSupportResourcesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();
        var page = await resources.ListAsync(request.Status, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await resources.CountAsync(request.Status, cancellationToken);
        return Result.Success(new PagedResponse<SupportResourceResponse>(
            page.Select(resource => SupportResourceResponse.From(resource, now)).ToList(), request.Page, request.PageSize, total));
    }
}
