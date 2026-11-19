using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Identity;

/// <summary>
/// The caller's household membership: their role, the member they are, and every member they may act for (themselves,
/// and as manager the members who have no account of their own).
/// </summary>
public sealed record MyHouseholdResponse(
    Guid HouseholdId,
    string Reference,
    HouseholdStatus Status,
    HouseholdRole Role,
    Guid MemberId,
    DateTimeOffset? RosterConfirmedAt,
    IReadOnlyList<Guid> ActableMemberIds);
