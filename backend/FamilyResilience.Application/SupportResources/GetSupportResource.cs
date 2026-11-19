using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

public sealed class GetSupportResource(ISupportResourceRepository resources, TimeProvider timeProvider)
{
    public async Task<Result<SupportResourceResponse>> HandleAsync(Guid resourceId, CancellationToken cancellationToken) =>
        await resources.GetAsync(SupportResourceId.From(resourceId), cancellationToken) is { } resource
            ? Result.Success(SupportResourceResponse.From(resource, timeProvider.GetUtcNow()))
            : Result.Failure<SupportResourceResponse>(SupportResourceErrors.NotFound);
}
