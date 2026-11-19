using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>Takes an approved version out of the directory. The row stays, for the record.</summary>
public sealed class RetireSupportResource(
    ISupportResourceRepository resources, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<SupportResourceResponse>> HandleAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var resource = await resources.GetAsync(SupportResourceId.From(resourceId), cancellationToken);
        if (resource is null)
        {
            return Result.Failure<SupportResourceResponse>(SupportResourceErrors.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        var retired = resource.Retire(now);
        if (retired.IsFailure)
        {
            return Result.Failure<SupportResourceResponse>(retired.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "support_resources.resource.retired", "support_resource", resourceId, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<SupportResourceResponse>(committed.Error!)
            : Result.Success(SupportResourceResponse.From(resource, now));
    }
}
