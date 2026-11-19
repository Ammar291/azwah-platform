using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assignments;

/// <summary>Assignments of the members the caller may act for, newest first, a page at a time.</summary>
public sealed class ListMyAssignments(ICallerAccess access, IAssignmentReadModel assignments)
{
    public async Task<Result<PagedResponse<ParticipantAssignmentResponse>>> HandleAsync(
        MyAssignmentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var members = await access.ActableMemberIdsAsync(cancellationToken);
        if (members.Count == 0)
        {
            return Result.Success(new PagedResponse<ParticipantAssignmentResponse>([], request.Page, request.PageSize, 0));
        }

        var page = await assignments.ListAsync(
            members, request.Status, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await assignments.CountAsync(members, request.Status, cancellationToken);
        return Result.Success(new PagedResponse<ParticipantAssignmentResponse>(page, request.Page, request.PageSize, total));
    }
}
