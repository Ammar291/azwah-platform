namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Masks the identifying parts of an upstream profile so a person can recognise their own details without the full
/// values leaving the provider. Anything that does not look like the expected format is dropped (null), never passed
/// through.
/// </summary>
internal static class ProfileMasking
{
    public const char Bullet = '•';

    /// <summary>
    /// An Emirates ID (784-YYYY-NNNNNNN-C, 15 digits) as <c>784-••••-•••••42-1</c>: the country prefix, the last two
    /// digits of the seven-digit block and the check digit. The birth year and the rest of the number are hidden.
    /// </summary>
    public static string? EmiratesId(string? value)
    {
        var digits = DigitsOnly(value, allowed: "- ");
        if (digits is not { Length: 15 } || !digits.StartsWith("784", StringComparison.Ordinal))
        {
            return null;
        }

        return $"784-{Bullets(4)}-{Bullets(5)}{digits[12..14]}-{digits[14]}";
    }

    /// <summary>
    /// A mobile number (digits, optionally with <c>+</c> or <c>00</c>) as <c>+971•••••••42</c>: the UAE country code and
    /// the last two digits. Other countries' numbers keep only the last two digits, since their code length varies.
    /// </summary>
    public static string? Mobile(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.StartsWith('+'))
        {
            text = text[1..];
        }
        else if (text.StartsWith("00", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        var digits = DigitsOnly(text, allowed: "- ");
        if (digits is not { Length: >= 8 and <= 15 })
        {
            return null;
        }

        return digits.StartsWith("971", StringComparison.Ordinal)
            ? $"+971{Bullets(digits.Length - 5)}{digits[^2..]}"
            : $"{Bullets(digits.Length - 2)}{digits[^2..]}";
    }

    /// <summary>An e-mail address as <c>t•••@example.ae</c>: the first character and the domain.</summary>
    public static string? Email(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 254 || text.Any(char.IsWhiteSpace))
        {
            return null;
        }

        var at = text.LastIndexOf('@');
        if (at < 1 || at == text.Length - 1 || char.IsSurrogate(text[0]))
        {
            return null;
        }

        return $"{text[0]}{Bullets(3)}@{text[(at + 1)..]}";
    }

    /// <summary>The OpenID Connect <c>gender</c> values, when recognisable: <c>male</c> or <c>female</c>.</summary>
    public static string? Gender(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "MALE" or "M" => "male",
        "FEMALE" or "F" => "female",
        _ => null,
    };

    private static string Bullets(int count) => new(Bullet, count);

    private static string? DigitsOnly(string? value, string allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.All(character => char.IsAsciiDigit(character) || allowed.Contains(character, StringComparison.Ordinal))
            ? string.Concat(trimmed.Where(char.IsAsciiDigit))
            : null;
    }
}
