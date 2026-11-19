using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>Replaces the content of a draft. An approved or retired version never changes: draft a new version instead.</summary>
public sealed class UpdateSupportResourceDraft(
    ISupportResourceRepository resources, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<SupportResourceResponse>> HandleAsync(
        Guid resourceId, UpdateSupportResourceDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resource = await resources.GetAsync(SupportResourceId.From(resourceId), cancellationToken);
        if (resource is null)
        {
            return Result.Failure<SupportResourceResponse>(SupportResourceErrors.NotFound);
        }

        var content = SupportResourceContentFactory.Create(request);
        if (content.IsFailure)
        {
            return Result.Failure<SupportResourceResponse>(content.Error!);
        }

        var now = timeProvider.GetUtcNow();
        var changed = resource.UpdateDraft(content.Value, now);
        if (changed.IsFailure)
        {
            return Result.Failure<SupportResourceResponse>(changed.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "support_resources.resource.updated", "support_resource", resourceId, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<SupportResourceResponse>(committed.Error!)
            : Result.Success(SupportResourceResponse.From(resource, now));
    }
}
