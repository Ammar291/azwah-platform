using FamilyResilience.Domain.Analytics;

namespace FamilyResilience.Application.Analytics;

public interface IExportJobRepository
{
    void Add(ExportJob job);

    Task<ExportJob?> GetAsync(ExportJobId id, CancellationToken cancellationToken = default);

    /// <summary>A page of the exports one subject requested, newest first, read without tracking.</summary>
    Task<IReadOnlyList<ExportJob>> ListRequestedByAsync(string subject, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountRequestedByAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>The subject's exports that are queued or running.</summary>
    Task<int> CountActiveRequestedByAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>The subject's exports requested at or after <paramref name="since"/>, whatever became of them.</summary>
    Task<int> CountRequestedSinceAsync(string subject, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Deletes failed and expired exports last changed before <paramref name="before"/> (their files are gone).</summary>
    Task<int> DeleteFinishedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default);

    /// <summary>Completed exports whose expiry has passed, oldest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<ExportJob>> ListExpiredAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default);

    /// <summary>Queued or running exports last changed before <paramref name="before"/>, oldest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<ExportJob>> ListStalledAsync(DateTimeOffset before, int take, CancellationToken cancellationToken = default);
}
