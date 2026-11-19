using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Households;

[DomainEventType("households.household_created.v1")]
public sealed record HouseholdCreated(HouseholdId HouseholdId, DateTimeOffset OccurredAt) : IDomainEvent;
