using System.Text;

namespace FamilyResilience.Domain.Safeguarding;

/// <summary>
/// Folds text to a comparable form for the crisis phrase screen: lower case, Arabic letter variants and diacritics
/// folded, apostrophes dropped, every other non-letter and non-digit a single space.
/// </summary>
public static class CrisisTextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var raw in text.ToLowerInvariant())
        {
            var c = Fold(raw);
            if (c is null)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c.Value))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(c.Value);
            }
            else if (c.Value != '\'')
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>Returns the folded character, ' for an apostrophe, a space for a separator, or null to drop it.</summary>
    private static char? Fold(char c)
    {
        // Tatweel, diacritics, Quranic annotation marks and zero-width joiners vanish without splitting the word.
        if (c is 'ـ' or (>= 'ً' and <= 'ٟ') or 'ٰ' or (>= 'ۖ' and <= 'ۭ') or '‌' or '‍')
        {
            return null;
        }

        if (c is '’' or '‘' or 'ʼ' or '`' or '´')
        {
            return '\'';
        }

        return c switch
        {
            'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
            'ة' => 'ه',
            'ى' => 'ي',
            'ؤ' => 'و',
            'ئ' => 'ي',
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            _ => c,
        };
    }
}
