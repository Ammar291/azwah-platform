using System.Security.Cryptography;
using System.Text;
using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.UnitTests.Support;

/// <summary>
/// SHA-256 of a content file's question and answer text, one line per item or option. The sources the content was
/// copied from are not in this repository (D-150); a fingerprint pins the copied text as it was when it was checked
/// against them, so any later edit to the text fails a test.
/// </summary>
internal static class ContentFingerprint
{
    /// <summary>One line per item: code, English prompt, Arabic prompt.</summary>
    public static string OfPrompts(IEnumerable<ContentItemDto> items) =>
        Of(items.Select(item => $"{item.Code}\t{item.Prompt.En}\t{item.Prompt.Ar ?? string.Empty}"));

    /// <summary>One line per answer option: item code, option code, English label, Arabic label.</summary>
    public static string OfOptions(IEnumerable<ContentItemDto> items) =>
        Of(items.SelectMany(item => item.Options.Select(option =>
            $"{item.Code}\t{option.Code}\t{option.Label.En}\t{option.Label.Ar ?? string.Empty}")));

    private static string Of(IEnumerable<string> lines) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
}
