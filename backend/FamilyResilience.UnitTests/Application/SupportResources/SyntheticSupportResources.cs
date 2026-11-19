using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.UnitTests.Application.SupportResources;

/// <summary>Builds synthetic directory entries for tests.</summary>
internal static class SyntheticSupportResources
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    public static SupportResource Make(
        string code = "synthetic_code",
        string summaryEn = "Synthetic summary",
        string summaryAr = "ملخص تجريبي",
        string[]? categories = null,
        string[]? audiences = null)
    {
        var content = SupportResourceContent.Create(
            "مورد تجريبي", "Synthetic resource", summaryAr, summaryEn, "جهة تجريبية", "Synthetic provider",
            "https://example.org/" + code, categories ?? ["synthetic_support"], audiences ?? ["synthetic_audience"],
            ["ar", "en"], SupportResourceVisibility.Everyone);
        return SupportResource.CreateDraft(code, 1, content.Value, Now).Value;
    }
}
