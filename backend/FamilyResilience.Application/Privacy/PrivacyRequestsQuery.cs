using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

/// <summary>Query string for the staff list: <c>?status=open&amp;page=1&amp;pageSize=20</c> (no status: both).</summary>
public sealed class PrivacyRequestsQuery : IPageRequest
{
    public PrivacyRequestStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
