namespace FamilyResilience.Application.Abstractions;

/// <summary>Query string <c>?page=1&amp;pageSize=20</c> (page size at most 100).</summary>
public sealed class PageRequest : IPageRequest
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
