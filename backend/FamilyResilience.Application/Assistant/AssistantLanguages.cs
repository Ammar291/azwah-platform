using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

/// <summary>The languages the assistant and the consultant answer in, as the app names them.</summary>
public static class AssistantLanguages
{
    public const string English = "en";
    public const string Arabic = "ar";

    public static bool IsSupported(string? language) => language is English or Arabic;

    /// <summary>The text in <paramref name="language"/>; English when the Arabic is missing (only drafts lack it).</summary>
    internal static string Pick(LocalizedText text, string language) =>
        language == Arabic ? text.Arabic ?? text.English : text.English;

    internal static string Pick(TextDto text, string language) =>
        language == Arabic ? text.Ar ?? text.En : text.En;
}
