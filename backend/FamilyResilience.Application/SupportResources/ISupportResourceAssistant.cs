namespace FamilyResilience.Application.SupportResources;

/// <summary>Chooses which approved directory entries to show for a search. Never writes text a family sees.</summary>
public interface ISupportResourceAssistant
{
    Task<SupportResourceDiscovery> DiscoverAsync(SupportResourceQuery query, CancellationToken cancellationToken);
}
