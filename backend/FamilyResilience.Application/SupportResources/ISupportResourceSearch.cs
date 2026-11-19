using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// Reads the directory as participants and staff may see it: approved, before the review date and visible to the
/// caller. Deterministic for a given directory. Every method returns untracked aggregates.
/// </summary>
public interface ISupportResourceSearch
{
    /// <summary>One page of the resources discoverable at <paramref name="now"/> and visible, ordered by code.</summary>
    Task<IReadOnlyList<SupportResource>> ListAsync(
        IReadOnlyCollection<SupportResourceVisibility> visibilities,
        DateTimeOffset now,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>How many resources are discoverable at <paramref name="now"/> and visible.</summary>
    Task<int> CountAsync(
        IReadOnlyCollection<SupportResourceVisibility> visibilities, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Matching resources in rank order, at most <see cref="SupportResourceSearchCriteria.Limit"/>.</summary>
    Task<IReadOnlyList<SupportResource>> SearchAsync(
        SupportResourceSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>The resource when it is discoverable at <paramref name="now"/> and visible; otherwise null.</summary>
    Task<SupportResource?> GetDiscoverableAsync(
        SupportResourceId id,
        IReadOnlyCollection<SupportResourceVisibility> visibilities,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
