using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

public interface ISupportResourceRepository
{
    void Add(SupportResource resource);

    /// <summary>Tracked.</summary>
    Task<SupportResource?> GetAsync(SupportResourceId id, CancellationToken cancellationToken = default);

    /// <summary>The approved version of a code, tracked; null when none is approved.</summary>
    Task<SupportResource?> GetApprovedAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>The highest version number used for a code plus one, or 1 when the code is new.</summary>
    Task<int> NextVersionNumberAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Untracked, ordered by code then version number descending.</summary>
    Task<IReadOnlyList<SupportResource>> ListAsync(
        SupportResourceStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(SupportResourceStatus? status, CancellationToken cancellationToken = default);
}
