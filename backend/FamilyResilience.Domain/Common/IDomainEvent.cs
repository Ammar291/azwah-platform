namespace FamilyResilience.Domain.Common;

/// <summary>
/// Something that happened to an aggregate. It is saved to the outbox in the same transaction as the change. Events
/// carry identifiers, enum values, numbers and times only: never answers, names or other free text.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
