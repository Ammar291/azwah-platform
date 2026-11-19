using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

/// <summary>
/// Records a family's request about its data for a member the caller may act for (their own member by default),
/// audited as <c>privacy.request.created</c>. One open request per member and type: a second answers
/// <c>privacy.request_already_open</c>. Anyone else's member answers <c>households.member_not_found</c>.
/// </summary>
public sealed class CreatePrivacyRequest(
    ICallerAccess access,
    ICurrentUser currentUser,
    IPrivacyRequestRepository requests,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<PrivacyRequestResponse>> HandleAsync(CreatePrivacyRequestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = await access.GetCallerAsync(cancellationToken);
        if (caller?.Household is not { } household || household.FindActiveMembership(caller.UserId) is not { } membership)
        {
            return Result.Failure<PrivacyRequestResponse>(HouseholdErrors.MemberNotFound);
        }

        var memberId = request.MemberId is { } id ? FamilyMemberId.From(id) : membership.FamilyMemberId;
        if (!await access.CanActForMemberAsync(memberId, cancellationToken))
        {
            return Result.Failure<PrivacyRequestResponse>(HouseholdErrors.MemberNotFound);
        }

        var type = request.Type ?? throw new InvalidOperationException("The validator requires a type.");
        var created = PrivacyRequest.Create(household.Id, memberId, type, currentUser.SubjectId, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<PrivacyRequestResponse>(created.Error!);
        }

        requests.Add(created.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "privacy.request.created", "privacy_request", created.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<PrivacyRequestResponse>(committed.Error!)
            : Result.Success(created.Value.ToResponse());
    }
}
