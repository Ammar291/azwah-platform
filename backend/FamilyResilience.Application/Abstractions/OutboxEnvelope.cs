namespace FamilyResilience.Application.Abstractions;

/// <summary>One outbox message as a handler sees it. The payload holds IDs, enum values, numbers and times only.</summary>
public sealed record OutboxEnvelope(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt);
