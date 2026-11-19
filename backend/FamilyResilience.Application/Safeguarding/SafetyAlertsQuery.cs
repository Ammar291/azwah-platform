using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Query string for the staff list: <c>?status=unresolved&amp;category=harm_to_others&amp;page=1&amp;pageSize=20</c>. No
/// status or category means all of them; a missing page is 1 and a missing page size 20. Validated through
/// <see cref="IPageRequest"/> and <see cref="PageRules"/>.
/// </summary>
public sealed record SafetyAlertsQuery(SafetyAlertStatusFilter? Status, SafetyConcern? Category, int? Page, int? PageSize) : IPageRequest
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 20;

    int IPageRequest.Page => Page ?? DefaultPage;

    int IPageRequest.PageSize => PageSize ?? DefaultPageSize;
}
