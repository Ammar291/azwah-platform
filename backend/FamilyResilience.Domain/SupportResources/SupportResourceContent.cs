using System.Text.RegularExpressions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.SupportResources;

/// <summary>
/// The editable content of a support resource, validated as a whole. Arabic may be missing in a draft; approval
/// requires <see cref="IsComplete"/>.
/// </summary>
public sealed partial record SupportResourceContent
{
    public const int MaxNameLength = 200;
    public const int MaxSummaryLength = 1000;
    public const int MaxUrlLength = 500;
    public const int MaxCategories = 10;
    public const int MaxAudiences = 10;
    public const int MaxServiceLanguages = 5;
    private const string HttpsPrefix = "https://";

    private SupportResourceContent(
        LocalizedText name,
        LocalizedText summary,
        LocalizedText providerName,
        string url,
        IReadOnlyList<string> categories,
        IReadOnlyList<string> audiences,
        IReadOnlyList<string> serviceLanguages,
        SupportResourceVisibility visibility)
    {
        Name = name;
        Summary = summary;
        ProviderName = providerName;
        Url = url;
        Categories = categories;
        Audiences = audiences;
        ServiceLanguages = serviceLanguages;
        Visibility = visibility;
    }

    public LocalizedText Name { get; }

    public LocalizedText Summary { get; }

    public LocalizedText ProviderName { get; }

    public string Url { get; }

    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyList<string> Audiences { get; }

    public IReadOnlyList<string> ServiceLanguages { get; }

    public SupportResourceVisibility Visibility { get; }

    public bool IsComplete => Name.IsComplete && Summary.IsComplete && ProviderName.IsComplete;

    public static Result<SupportResourceContent> Create(
        string? nameAr,
        string? nameEn,
        string? summaryAr,
        string? summaryEn,
        string? providerNameAr,
        string? providerNameEn,
        string? url,
        IReadOnlyList<string>? categories,
        IReadOnlyList<string>? audiences,
        IReadOnlyList<string>? serviceLanguages,
        SupportResourceVisibility visibility)
    {
        var name = LocalizedText.CreateDraft(nameAr, nameEn, MaxNameLength);
        if (name.IsFailure)
        {
            return Result.Failure<SupportResourceContent>(name.Error!);
        }

        var summary = LocalizedText.CreateDraft(summaryAr, summaryEn, MaxSummaryLength);
        if (summary.IsFailure)
        {
            return Result.Failure<SupportResourceContent>(summary.Error!);
        }

        var provider = LocalizedText.CreateDraft(providerNameAr, providerNameEn, MaxNameLength);
        if (provider.IsFailure)
        {
            return Result.Failure<SupportResourceContent>(provider.Error!);
        }

        var trimmedUrl = url?.Trim();
        if (!IsPlainHttpsUrl(trimmedUrl))
        {
            return Result.Failure<SupportResourceContent>(SupportResourceErrors.UrlInvalid);
        }

        if (categories is null || categories.Count is 0 or > MaxCategories || !AreDistinctSnakeCodes(categories))
        {
            return Result.Failure<SupportResourceContent>(SupportResourceErrors.CategoriesInvalid);
        }

        if (audiences is null || audiences.Count > MaxAudiences || !AreDistinctSnakeCodes(audiences))
        {
            return Result.Failure<SupportResourceContent>(SupportResourceErrors.AudiencesInvalid);
        }

        if (serviceLanguages is null
            || serviceLanguages.Count is 0 or > MaxServiceLanguages
            || !serviceLanguages.All(l => l is not null && LanguageCode().IsMatch(l))
            || !AreDistinct(serviceLanguages))
        {
            return Result.Failure<SupportResourceContent>(SupportResourceErrors.ServiceLanguagesInvalid);
        }

        if (!Enum.IsDefined(visibility))
        {
            return Result.Failure<SupportResourceContent>(SupportResourceErrors.VisibilityInvalid);
        }

        return Result.Success(new SupportResourceContent(
            name.Value, summary.Value, provider.Value, LowercaseScheme(trimmedUrl!), categories.ToArray(),
            audiences.ToArray(), serviceLanguages.ToArray(), visibility));
    }

    // Uri.Scheme is always lowercase, so "HTTPS://…" passes the Uri check. The database check (url LIKE 'https://%') is
    // case-sensitive, so the URL must start with "https://" in any case, and is stored with that prefix in lowercase and
    // the rest exactly as typed.
    private static bool IsPlainHttpsUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url.Length <= MaxUrlLength
        && !url.Any(char.IsWhiteSpace)
        && url.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0;

    private static string LowercaseScheme(string url) => HttpsPrefix + url[HttpsPrefix.Length..];

    private static bool AreDistinctSnakeCodes(IReadOnlyList<string> codes) =>
        codes.All(Codes.IsSnakeCode) && AreDistinct(codes);

    private static bool AreDistinct(IReadOnlyList<string> values) =>
        values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    [GeneratedRegex(@"^[a-z]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCode();
}
