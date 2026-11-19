using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>The whole content of a draft, replacing what it held. The code and version number never change.</summary>
public sealed record UpdateSupportResourceDraftRequest(
    string? NameAr,
    string? NameEn,
    string? SummaryAr,
    string? SummaryEn,
    string? ProviderNameAr,
    string? ProviderNameEn,
    string? Url,
    IReadOnlyList<string>? Categories,
    IReadOnlyList<string>? Audiences,
    IReadOnlyList<string>? ServiceLanguages,
    SupportResourceVisibility? Visibility) : ISupportResourceContentRequest;
