using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Households;

[DomainEventType("households.family_member_deactivated.v1")]
public sealed record FamilyMemberDeactivated(
    HouseholdId HouseholdId, FamilyMemberId FamilyMemberId, DateTimeOffset OccurredAt) : IDomainEvent;
