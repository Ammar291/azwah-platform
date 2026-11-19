using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

/// <summary>A program administrator closes a request with its resolution, audited as <c>privacy.request.closed</c>.</summary>
public sealed class ClosePrivacyRequest(
    IPrivacyRequestRepository requests,
    IPrivacyRequestReadModel readModel,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const string Closed = "privacy.request.closed";
    public const string ResourceType = "privacy_request";

    public async Task<Result<AdminPrivacyRequestResponse>> HandleAsync(
        Guid requestId, ClosePrivacyRequestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = PrivacyRequestId.From(requestId);
        var privacyRequest = await requests.GetAsync(id, cancellationToken);
        if (privacyRequest is null)
        {
            return Result.Failure<AdminPrivacyRequestResponse>(PrivacyErrors.RequestNotFound);
        }

        var resolution = request.Resolution ?? throw new InvalidOperationException("The validator requires a resolution.");
        var closed = privacyRequest.Close(resolution, currentUser.SubjectId, timeProvider.GetUtcNow());
        if (closed.IsFailure)
        {
            return Result.Failure<AdminPrivacyRequestResponse>(closed.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, Closed, ResourceType, requestId, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<AdminPrivacyRequestResponse>(committed.Error!);
        }

        return Result.Success(await readModel.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("A closed request must still exist."));
    }
}
