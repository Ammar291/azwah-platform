using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Households;

[DomainEventType("households.household_withdrawn.v1")]
public sealed record HouseholdWithdrawn(HouseholdId HouseholdId, DateTimeOffset OccurredAt) : IDomainEvent;
