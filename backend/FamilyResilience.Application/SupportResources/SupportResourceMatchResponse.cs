using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>One directory entry in a search answer, rendered from approved fields only.</summary>
public sealed record SupportResourceMatchResponse(
    Guid Id,
    string Code,
    int VersionNumber,
    int Rank,
    string NameAr,
    string NameEn,
    string SummaryAr,
    string SummaryEn,
    string ProviderNameAr,
    string ProviderNameEn,
    string Url,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> ServiceLanguages)
{
    public static SupportResourceMatchResponse From(SupportResource resource, int rank)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new SupportResourceMatchResponse(
            resource.Id.Value,
            resource.Code,
            resource.VersionNumber,
            rank,
            resource.Name.Arabic ?? string.Empty,
            resource.Name.English,
            resource.Summary.Arabic ?? string.Empty,
            resource.Summary.English,
            resource.ProviderName.Arabic ?? string.Empty,
            resource.ProviderName.English,
            resource.Url,
            resource.Categories,
            resource.Audiences,
            resource.ServiceLanguages);
    }
}
