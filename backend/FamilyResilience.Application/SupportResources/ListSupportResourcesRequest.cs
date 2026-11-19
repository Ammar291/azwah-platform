using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>Query string for the directory: an optional status, ordered by code then newest version.</summary>
public sealed class ListSupportResourcesRequest : IPageRequest
{
    public SupportResourceStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
