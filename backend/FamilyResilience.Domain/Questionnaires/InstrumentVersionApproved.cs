using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

[DomainEventType("questionnaires.instrument_version_approved.v1")]
public sealed record InstrumentVersionApproved(
    InstrumentVersionId InstrumentVersionId, InstrumentId InstrumentId, DateTimeOffset OccurredAt) : IDomainEvent;
