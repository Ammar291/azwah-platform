using System.Text.RegularExpressions;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The last step before a reply reaches the app (D-126): the app shows plain text, so Markdown becomes plain text
/// (emphasis and code marks dropped, headings unmarked, bullets become hyphens, links become "text (address)"), spacing
/// is tidied, a reply with no letter or digit becomes empty (the turn then fails), and the reply is cut at
/// <see cref="MaxLength"/> characters. Deterministic, and it never adds words.
/// </summary>
public static partial class AssistantReplyGuard
{
    public const int MaxLength = 4_000;

    public static string Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        value = Link().Replace(value, match => match.Groups["text"].Value == match.Groups["url"].Value
            ? match.Groups["url"].Value
            : $"{match.Groups["text"].Value} ({match.Groups["url"].Value})");
        value = Heading().Replace(value, string.Empty);
        value = Strong().Replace(value, "${inner}");
        value = Emphasis().Replace(value, "${inner}");
        value = Bullet().Replace(value, "${indent}- ");
        value = value.Replace("`", string.Empty, StringComparison.Ordinal);
        value = HorizontalRule().Replace(value, string.Empty);
        value = TrailingSpaces().Replace(value, "\n");
        value = BlankLines().Replace(value, "\n\n").Trim();
        if (!value.Any(char.IsLetterOrDigit))
        {
            // Only marks or symbols: nothing a person could read, so the turn counts as empty.
            return string.Empty;
        }

        return value.Length <= MaxLength ? value : value[..MaxLength].TrimEnd();
    }

    [GeneratedRegex(@"\[(?<text>[^\]\n]{1,300})\]\((?<url>https?://[^\s)]{1,500})\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^[ \t]{0,3}#{1,6}[ \t]+", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?<indent>[ \t]*)[*+•▪◦●][ \t]+(?=\S)", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"(\*\*|__)(?<inner>\S(?:.*?\S)?)\1", RegexOptions.Singleline)]
    private static partial Regex Strong();

    [GeneratedRegex(@"(?<![\*\w])\*(?<inner>[^\s*](?:[^*\n]*?[^\s*])?)\*(?![\*\w])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"^[ \t]*(?:-{3,}|\*{3,}|_{3,})[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
