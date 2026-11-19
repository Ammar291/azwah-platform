namespace FamilyResilience.Application.SupportResources;

/// <summary>The directory search itself: keyword matching over approved entries, ranked the same way every time.</summary>
public sealed class DeterministicSupportResourceAssistant(ISupportResourceSearch search, TimeProvider timeProvider)
    : ISupportResourceAssistant
{
    public async Task<SupportResourceDiscovery> DiscoverAsync(SupportResourceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Terms.Count == 0 && query.Categories.Count == 0)
        {
            return new SupportResourceDiscovery(DiscoveryMode.Deterministic, null, []);
        }

        var matches = await search.SearchAsync(
            new SupportResourceSearchCriteria(
                query.Terms,
                query.Categories,
                query.Audiences,
                query.Visibilities,
                timeProvider.GetUtcNow(),
                TermCoverage.Half,
                query.Limit),
            cancellationToken);
        return new SupportResourceDiscovery(DiscoveryMode.Deterministic, null, matches);
    }
}
