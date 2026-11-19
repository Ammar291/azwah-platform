using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Analytics;

/// <summary>Query string for the participation list: optional status, roster confirmation and household reference.</summary>
public sealed class ParticipationRequest : IPageRequest
{
    public HouseholdStatus? Status { get; init; }

    public bool? RosterConfirmed { get; init; }

    /// <summary>A household reference such as <c>HH-7K3M-Q9TX</c>, as read out on the phone.</summary>
    public string? Reference { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
