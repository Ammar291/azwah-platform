using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Privacy;

/// <summary>The privacy requests of the members the caller may act for, newest first, a page at a time.</summary>
public sealed class ListMyPrivacyRequests(ICallerAccess access, IPrivacyRequestRepository requests)
{
    public async Task<Result<PagedResponse<PrivacyRequestResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var members = await access.ActableMemberIdsAsync(cancellationToken);
        if (members.Count == 0)
        {
            return Result.Success(new PagedResponse<PrivacyRequestResponse>([], request.Page, request.PageSize, 0));
        }

        var page = await requests.ListForMembersAsync(members, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        return Result.Success(new PagedResponse<PrivacyRequestResponse>(
            page.Select(item => item.ToResponse()).ToList(),
            request.Page,
            request.PageSize,
            await requests.CountForMembersAsync(members, cancellationToken)));
    }
}
