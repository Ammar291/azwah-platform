using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Households;

/// <summary>
/// A household as its manager sees it. The reference is safe to read out on the phone; it is never derived from an ID.
/// </summary>
public sealed record HouseholdResponse(
    Guid Id,
    string Reference,
    HouseholdStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RosterConfirmedAt,
    int ActiveMemberCount);
