using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Households;

/// <summary>
/// A merge patch of the household. The only change a household can make to itself is to withdraw from the study
/// (<c>{"status": "withdrawn"}</c>); the reference never changes.
/// </summary>
public sealed record UpdateHouseholdRequest(HouseholdStatus? Status);
