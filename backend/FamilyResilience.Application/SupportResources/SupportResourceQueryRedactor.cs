using System.Text.RegularExpressions;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// Strips what could identify a person from search text before any model sees it: emails, links, Emirates-ID-shaped
/// strings and every digit. Each removal leaves a space so neighbouring words do not fuse.
/// </summary>
public static partial class SupportResourceQueryRedactor
{
    public const int MaxLength = 200;

    public static string Redact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = Email().Replace(text, " ");
        result = Url().Replace(result, " ");
        result = EmiratesId().Replace(result, " ");
        result = Digits().Replace(result, " ");
        result = Whitespace().Replace(result, " ").Trim();

        if (result.Length > MaxLength)
        {
            var cut = char.IsHighSurrogate(result[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            result = result[..cut].TrimEnd();
        }

        return result;
    }

    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    // \p{Nd} is every Unicode decimal digit: Western, Arabic-Indic, Extended Arabic-Indic, full-width and the rest.
    [GeneratedRegex(@"784(?:[-\s]?\p{Nd}){12}", RegexOptions.CultureInvariant)]
    private static partial Regex EmiratesId();

    [GeneratedRegex(@"\p{Nd}", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
