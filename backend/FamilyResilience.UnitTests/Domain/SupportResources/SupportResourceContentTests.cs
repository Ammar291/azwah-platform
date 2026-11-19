using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.SupportResources;

public sealed class SupportResourceContentTests
{
    internal static Result<SupportResourceContent> Build(
        string? nameAr = "خدمة تجريبية",
        string? summaryAr = "ملخص تجريبي",
        string? providerAr = "جهة تجريبية",
        string? url = "https://example.org/synthetic",
        IReadOnlyList<string>? categories = null,
        IReadOnlyList<string>? audiences = null,
        IReadOnlyList<string>? languages = null,
        SupportResourceVisibility visibility = SupportResourceVisibility.Everyone) =>
        SupportResourceContent.Create(
            nameAr, "Synthetic service", summaryAr, "Synthetic summary", providerAr, "Synthetic provider", url,
            categories ?? ["financial_counselling"], audiences ?? ["parents"], languages ?? ["ar", "en"], visibility);

    private static void AssertError(Result result, string code)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error!.Code);
    }

    private static List<string> Codes(int count) => Enumerable.Range(0, count).Select(i => $"code_{i}").ToList();

    [Fact]
    public void A_complete_bilingual_resource_is_accepted()
    {
        var content = Ok(Build(url: "  https://example.org/synthetic  "));

        Assert.True(content.IsComplete);
        Assert.Equal("https://example.org/synthetic", content.Url);
        Assert.Equal("Synthetic service", content.Name.English);
        Assert.Equal(["financial_counselling"], content.Categories);
        Assert.Equal(SupportResourceVisibility.Everyone, content.Visibility);
    }

    [Fact]
    public void Arabic_may_be_missing_in_a_draft()
    {
        var content = Ok(Build(summaryAr: null));

        Assert.False(content.IsComplete);
    }

    [Theory]
    [InlineData("http://example.org")]
    [InlineData("ftp://example.org")]
    [InlineData("example.org/page")]
    [InlineData("https://user:secret@example.org")]
    [InlineData("https://exa mple.org")]
    [InlineData("https://")]
    [InlineData(null)]
    public void A_url_that_is_not_plain_https_is_refused(string? url) =>
        AssertError(Build(url: url), "support_resources.url_invalid");

    // The database check (url LIKE 'https://%') is case-sensitive, so a capitalised scheme is stored in lowercase.
    [Theory]
    [InlineData("Https://example.org/x", "https://example.org/x")]
    [InlineData("HTTPS://example.org/x", "https://example.org/x")]
    [InlineData("  hTtPs://example.org/x  ", "https://example.org/x")]
    [InlineData("HTTPS://Example.org/Path?Q=A", "https://Example.org/Path?Q=A")]
    public void A_capitalised_https_scheme_is_stored_in_lowercase(string url, string stored) =>
        Assert.Equal(stored, Ok(Build(url: url)).Url);

    [Theory]
    [InlineData("HTTP://example.org")]
    [InlineData("Ftp://example.org")]
    public void A_capitalised_scheme_that_is_not_https_is_refused(string url) =>
        AssertError(Build(url: url), "support_resources.url_invalid");

    [Fact]
    public void A_url_over_500_characters_is_refused() =>
        AssertError(Build(url: "https://example.org/" + new string('a', 481)), "support_resources.url_invalid");

    [Fact]
    public void A_url_of_exactly_500_characters_is_accepted() =>
        Ok(Build(url: "https://example.org/" + new string('a', 480)));

    [Fact]
    public void Bad_categories_are_refused()
    {
        AssertError(Build(categories: []), "support_resources.categories_invalid");
        AssertError(Build(categories: Codes(11)), "support_resources.categories_invalid");
        AssertError(Build(categories: ["a_b", "a_b"]), "support_resources.categories_invalid");
        AssertError(Build(categories: ["Not Snake"]), "support_resources.categories_invalid");
        Ok(Build(categories: Codes(10)));
    }

    [Fact]
    public void Audiences_may_be_empty() => Assert.Empty(Ok(Build(audiences: [])).Audiences);

    [Fact]
    public void Bad_audiences_are_refused()
    {
        AssertError(Build(audiences: Codes(11)), "support_resources.audiences_invalid");
        AssertError(Build(audiences: ["a_b", "a_b"]), "support_resources.audiences_invalid");
        AssertError(Build(audiences: ["Bad Code"]), "support_resources.audiences_invalid");
    }

    [Fact]
    public void Bad_service_languages_are_refused()
    {
        AssertError(Build(languages: []), "support_resources.service_languages_invalid");
        AssertError(Build(languages: ["ara"]), "support_resources.service_languages_invalid");
        AssertError(Build(languages: ["AR"]), "support_resources.service_languages_invalid");
        AssertError(Build(languages: ["ar", "ar"]), "support_resources.service_languages_invalid");
        AssertError(Build(languages: ["aa", "bb", "cc", "dd", "ee", "ff"]), "support_resources.service_languages_invalid");
    }

    [Fact]
    public void An_undefined_visibility_is_refused() =>
        AssertError(Build(visibility: (SupportResourceVisibility)7), "support_resources.visibility_invalid");

    [Fact]
    public void Text_errors_come_before_url_errors() =>
        AssertError(
            SupportResourceContent.Create(
                null, "", null, "s", null, "p", "http://x", ["a"], [], ["ar"], SupportResourceVisibility.Everyone),
            "common.text_required");

    [Fact]
    public void Url_errors_come_before_category_errors() =>
        AssertError(Build(url: "http://x", categories: []), "support_resources.url_invalid");
}
