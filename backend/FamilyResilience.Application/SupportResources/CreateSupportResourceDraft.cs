using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// Adds a draft to the directory: version 1 of a new code, or the next version of an existing one. A draft is never
/// shown to participants.
/// </summary>
public sealed class CreateSupportResourceDraft(
    ISupportResourceRepository resources, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<SupportResourceResponse>> HandleAsync(
        CreateSupportResourceDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var content = SupportResourceContentFactory.Create(request);
        if (content.IsFailure)
        {
            return Result.Failure<SupportResourceResponse>(content.Error!);
        }

        var code = request.Code ?? string.Empty;
        var now = timeProvider.GetUtcNow();
        var version = await resources.NextVersionNumberAsync(code, cancellationToken);
        var draft = SupportResource.CreateDraft(code, version, content.Value, now);
        if (draft.IsFailure)
        {
            return Result.Failure<SupportResourceResponse>(draft.Error!);
        }

        resources.Add(draft.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "support_resources.resource.drafted", "support_resource", draft.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<SupportResourceResponse>(committed.Error!)
            : Result.Success(SupportResourceResponse.From(draft.Value, now));
    }
}
