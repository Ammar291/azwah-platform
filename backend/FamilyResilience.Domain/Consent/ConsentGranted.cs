using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Consent;

[DomainEventType("consent.consent_granted.v1")]
public sealed record ConsentGranted(
    ConsentRecordId ConsentRecordId, FamilyMemberId FamilyMemberId, ConsentType ConsentType, DateTimeOffset OccurredAt)
    : IDomainEvent;
