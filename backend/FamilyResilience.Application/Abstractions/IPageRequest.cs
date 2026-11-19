namespace FamilyResilience.Application.Abstractions;

/// <summary>A query that asks for one page: <c>?page=1&amp;pageSize=20</c>. Validated by <see cref="PageRules"/>.</summary>
public interface IPageRequest
{
    int Page { get; }

    int PageSize { get; }
}
