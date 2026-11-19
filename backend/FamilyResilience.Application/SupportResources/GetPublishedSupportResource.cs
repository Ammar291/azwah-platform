using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>One directory entry for the caller: answers not found unless it is discoverable now and visible to them.</summary>
public sealed class GetPublishedSupportResource(
    ISupportResourceSearch search, ICurrentUser currentUser, TimeProvider timeProvider)
{
    public async Task<Result<PublishedSupportResourceResponse>> HandleAsync(Guid resourceId, CancellationToken cancellationToken) =>
        await search.GetDiscoverableAsync(
            SupportResourceId.From(resourceId),
            SupportResourceAccess.VisibilitiesFor(currentUser.Roles),
            timeProvider.GetUtcNow(),
            cancellationToken) is { } resource
            ? Result.Success(PublishedSupportResourceResponse.From(resource))
            : Result.Failure<PublishedSupportResourceResponse>(SupportResourceErrors.NotFound);
}
