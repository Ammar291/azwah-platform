using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Assistant;

/// <summary>The household: its public reference (such as <c>HH-7K3M-Q9TX</c>), status and whether its roster is confirmed.</summary>
public sealed record FamilyContextHousehold(string Reference, HouseholdStatus Status, bool RosterConfirmed);
