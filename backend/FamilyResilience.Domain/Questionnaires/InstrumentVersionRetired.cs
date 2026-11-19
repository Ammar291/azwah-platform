using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

[DomainEventType("questionnaires.instrument_version_retired.v1")]
public sealed record InstrumentVersionRetired(
    InstrumentVersionId InstrumentVersionId, InstrumentId InstrumentId, DateTimeOffset OccurredAt) : IDomainEvent;
