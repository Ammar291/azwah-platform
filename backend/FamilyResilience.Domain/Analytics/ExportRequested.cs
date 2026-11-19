using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Analytics;

/// <summary>A researcher asked for an export; its outbox handler writes the file.</summary>
[DomainEventType("analytics.export_requested.v1")]
public sealed record ExportRequested(ExportJobId ExportJobId, DateTimeOffset OccurredAt) : IDomainEvent;
