using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>The editable content a draft request carries; <see cref="SupportResourceContentRules"/> checks its shape.</summary>
public interface ISupportResourceContentRequest
{
    string? NameAr { get; }

    string? NameEn { get; }

    string? SummaryAr { get; }

    string? SummaryEn { get; }

    string? ProviderNameAr { get; }

    string? ProviderNameEn { get; }

    string? Url { get; }

    IReadOnlyList<string>? Categories { get; }

    IReadOnlyList<string>? Audiences { get; }

    IReadOnlyList<string>? ServiceLanguages { get; }

    SupportResourceVisibility? Visibility { get; }
}
