using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// Approves a draft with the caller as approver, a named source and a review date. A code has at most one approved
/// version, so the previous one is retired first, in two commits inside one transaction because the database checks
/// that rule row by row. Any failure rolls both back.
/// </summary>
public sealed class ApproveSupportResource(
    ISupportResourceRepository resources,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public Task<Result<SupportResourceResponse>> HandleAsync(
        Guid resourceId, ApproveSupportResourceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var resource = await resources.GetAsync(SupportResourceId.From(resourceId), token);
                if (resource is null)
                {
                    return Result.Failure<SupportResourceResponse>(SupportResourceErrors.NotFound);
                }

                if (resource.Status != SupportResourceStatus.Draft)
                {
                    return Result.Failure<SupportResourceResponse>(SupportResourceErrors.NotDraft);
                }

                var now = timeProvider.GetUtcNow();
                var approval = Approval.Create(currentUser.SubjectId, now, request.ApprovalSource);
                if (approval.IsFailure)
                {
                    return Result.Failure<SupportResourceResponse>(approval.Error!);
                }

                var previous = await resources.GetApprovedAsync(resource.Code, token);
                if (previous is not null)
                {
                    var retired = previous.Retire(now);
                    if (retired.IsFailure)
                    {
                        return Result.Failure<SupportResourceResponse>(retired.Error!);
                    }

                    var committedRetirement = await AuditedCommit.RunAsync(
                        auditLog, unitOfWork, "support_resources.resource.retired", "support_resource", previous.Id.Value, token);
                    if (committedRetirement.IsFailure)
                    {
                        return Result.Failure<SupportResourceResponse>(committedRetirement.Error!);
                    }
                }

                var approved = resource.Approve(
                    approval.Value, request.ReviewDueAt ?? throw new ArgumentException("ReviewDueAt is required.", nameof(request)), now);
                if (approved.IsFailure)
                {
                    return Result.Failure<SupportResourceResponse>(approved.Error!);
                }

                var committed = await AuditedCommit.RunAsync(
                    auditLog, unitOfWork, "support_resources.resource.approved", "support_resource", resource.Id.Value, token);
                return committed.IsFailure
                    ? Result.Failure<SupportResourceResponse>(committed.Error!)
                    : Result.Success(SupportResourceResponse.From(resource, now));
            },
            cancellationToken);
    }
}
