using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>An assistant's answer: real directory entries in the order to show them.</summary>
public sealed record SupportResourceDiscovery(
    DiscoveryMode Mode, DiscoveryFallback? Fallback, IReadOnlyList<SupportResource> Matches)
{
    public DiscoveryOutcome Outcome => Matches.Count == 0 ? DiscoveryOutcome.NoMatch : DiscoveryOutcome.Matched;
}
