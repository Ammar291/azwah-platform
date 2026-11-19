using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// A new draft of a directory entry. Drafting a code that already exists makes its next version. Arabic may be left
/// out until approval.
/// </summary>
public sealed record CreateSupportResourceDraftRequest(
    string? Code,
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
