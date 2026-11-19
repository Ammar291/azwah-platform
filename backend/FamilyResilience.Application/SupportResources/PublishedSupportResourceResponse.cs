using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>One approved directory entry, as a participant or staff member may read it.</summary>
public sealed record PublishedSupportResourceResponse(
    Guid Id,
    string Code,
    int VersionNumber,
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
    public static PublishedSupportResourceResponse From(SupportResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var match = SupportResourceMatchResponse.From(resource, 1);
        return new PublishedSupportResourceResponse(
            match.Id, match.Code, match.VersionNumber, match.NameAr, match.NameEn, match.SummaryAr, match.SummaryEn,
            match.ProviderNameAr, match.ProviderNameEn, match.Url, match.Categories, match.Audiences, match.ServiceLanguages);
    }
}
