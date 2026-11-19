using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Households;

[DomainEventType("households.roster_confirmed.v1")]
public sealed record RosterConfirmed(HouseholdId HouseholdId, DateTimeOffset OccurredAt) : IDomainEvent;
