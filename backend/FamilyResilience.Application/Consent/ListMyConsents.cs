using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;

namespace FamilyResilience.Application.Consent;

/// <summary>Consent records of the members the caller may act for, newest first, a page at a time.</summary>
public sealed class ListMyConsents(ICallerAccess access, IConsentRecordRepository consents, IConsentPolicyVersionRepository policies)
{
    public async Task<Result<PagedResponse<ConsentRecordResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var members = await access.ActableMemberIdsAsync(cancellationToken);
        if (members.Count == 0)
        {
            return Result.Success(new PagedResponse<ConsentRecordResponse>([], request.Page, request.PageSize, 0));
        }

        var page = await consents.ListForMembersAsync(members, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await consents.CountForMembersAsync(members, cancellationToken);
        var versions = new Dictionary<ConsentPolicyVersionId, int>();
        foreach (var policyId in page.Select(record => record.ConsentPolicyVersionId).Distinct())
        {
            versions[policyId] = (await policies.GetAsync(policyId, cancellationToken))?.VersionNumber
                ?? throw new InvalidOperationException("A consent record's policy must exist.");
        }

        return Result.Success(new PagedResponse<ConsentRecordResponse>(
            page.Select(record => record.ToResponse(versions[record.ConsentPolicyVersionId])).ToList(),
            request.Page,
            request.PageSize,
            total));
    }
}
