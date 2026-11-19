using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Submitted check-ins of the members the caller may act for, most recent first, a page at a time. The list carries
/// tiers for the household's head only (D-103), so every read is audited, like reading a result.
/// </summary>
public sealed class ListAssessmentHistory(
    ICallerAccess access, IParticipantAccess participant, ISessionReadModel sessions, IAuditLog auditLog)
{
    public async Task<Result<PagedResponse<AssessmentHistoryItemResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var members = await access.ActableMemberIdsAsync(cancellationToken);
        var page = members.Count == 0
            ? new PagedResponse<AssessmentHistoryItemResponse>([], request.Page, request.PageSize, 0)
            : new PagedResponse<AssessmentHistoryItemResponse>(
                await sessions.ListHistoryAsync(members, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken),
                request.Page,
                request.PageSize,
                await sessions.CountHistoryAsync(members, cancellationToken));
        if (page.Items.Count > 0 && !await participant.IsHeadOfHouseholdAsync(cancellationToken))
        {
            page = page with { Items = [.. page.Items.Select(item => item with { TierCode = null })] };
        }

        var recorded = await auditLog.RecordAsync(
            new AuditRecord("sessions.history.read", "assessment_history", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure ? Result.Failure<PagedResponse<AssessmentHistoryItemResponse>>(recorded.Error!) : Result.Success(page);
    }
}
