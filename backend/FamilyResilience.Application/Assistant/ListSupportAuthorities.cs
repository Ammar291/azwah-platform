namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The knowledge graph's authorities for the All services page (D-115), minus any past their review date. Public information,
/// the same for everyone and linked to no answers, score or referral.
/// </summary>
public sealed class ListSupportAuthorities(ISupportKnowledgeGraph graph, TimeProvider timeProvider)
{
    public IReadOnlyList<SupportAuthorityResponse> Handle()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return graph.Current(today).Select(authority => new SupportAuthorityResponse(
            authority.Code,
            authority.Kind,
            authority.Name,
            authority.Purpose,
            authority.Emirates,
            authority.Website,
            authority.Phone,
            authority.Services.Select(service => new SupportServiceResponse(
                service.Code,
                service.Name,
                service.Summary,
                service.Procedure,
                service.Documents,
                service.Website,
                service.Via is { } code && code != authority.Code ? graph.FindAuthority(code)?.Name : null)).ToList(),
            authority.CheckedOn)).ToList();
    }
}
