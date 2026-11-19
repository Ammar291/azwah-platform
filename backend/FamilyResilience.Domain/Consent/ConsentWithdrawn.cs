using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Consent;

[DomainEventType("consent.consent_withdrawn.v1")]
public sealed record ConsentWithdrawn(
    ConsentRecordId ConsentRecordId, FamilyMemberId FamilyMemberId, ConsentType ConsentType, DateTimeOffset OccurredAt)
    : IDomainEvent;
