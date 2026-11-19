namespace FamilyResilience.Application.Abstractions;

/// <summary>Housekeeping on outbox messages for the retention job.</summary>
public interface IOutboxMaintenance
{
    /// <summary>Deletes processed messages processed before <paramref name="before"/>; pending and failed ones stay.</summary>
    Task<int> DeleteProcessedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
