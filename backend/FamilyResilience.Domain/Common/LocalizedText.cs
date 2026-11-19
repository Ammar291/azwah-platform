using System.Text.RegularExpressions;

namespace FamilyResilience.Domain.Common;

/// <summary>
/// Text shown to participants, in Arabic and English. English is always present. Arabic may be missing only in draft
/// questionnaire content (<see cref="CreateDraft"/>); publishing a version requires every text to be
/// <see cref="IsComplete"/>.
/// </summary>
public sealed partial record LocalizedText
{
    // EF Core binds this constructor when it reads rows.
    private LocalizedText(string? arabic, string english)
    {
        Arabic = arabic;
        English = english;
    }

    /// <summary>Null only in draft content whose Arabic has not been supplied yet.</summary>
    public string? Arabic { get; private init; }

    public string English { get; private init; }

    public bool IsComplete => Arabic is not null;

    /// <summary>Both languages, both required.</summary>
    public static Result<LocalizedText> Create(string? arabic, string? english, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        return string.IsNullOrWhiteSpace(arabic)
            ? Result.Failure<LocalizedText>(CommonErrors.TextRequired)
            : CreateDraft(arabic, english, maxLength);
    }

    /// <summary>English required; Arabic optional (blank counts as missing) but, when given, in Arabic script.</summary>
    public static Result<LocalizedText> CreateDraft(string? arabic, string? english, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        var trimmedArabic = string.IsNullOrWhiteSpace(arabic) ? null : arabic.Trim();
        var trimmedEnglish = english?.Trim();

        if (string.IsNullOrEmpty(trimmedEnglish))
        {
            return Result.Failure<LocalizedText>(CommonErrors.TextRequired);
        }

        if (trimmedEnglish.Length > maxLength || trimmedArabic?.Length > maxLength)
        {
            return Result.Failure<LocalizedText>(CommonErrors.TextTooLong);
        }

        if (trimmedArabic is not null && !ArabicScript().IsMatch(trimmedArabic))
        {
            return Result.Failure<LocalizedText>(CommonErrors.ArabicTextNotArabic);
        }

        return Result.Success(new LocalizedText(trimmedArabic, trimmedEnglish));
    }

    public bool FitsWithin(int maxLength) => (Arabic?.Length ?? 0) <= maxLength && English.Length <= maxLength;

    [GeneratedRegex(@"[؀-ۿ]", RegexOptions.CultureInvariant)]
    private static partial Regex ArabicScript();
}
