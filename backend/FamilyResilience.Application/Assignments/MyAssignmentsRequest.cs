using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Assignments;

/// <summary>Query string <c>?status=assigned&amp;page=1&amp;pageSize=20</c>.</summary>
public sealed class MyAssignmentsRequest : IPageRequest
{
    public AssignmentStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
