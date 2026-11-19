using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Domain.Common;

public sealed class LocalizedTextTests
{
    [Fact]
    public void Both_languages_are_trimmed_and_kept()
    {
        var result = LocalizedText.Create("  نص  ", " Text ", 50);

        Assert.True(result.IsSuccess);
        Assert.Equal("نص", result.Value.Arabic);
        Assert.Equal("Text", result.Value.English);
    }

    [Theory]
    [InlineData(null, "Text")]
    [InlineData("نص", null)]
    [InlineData(" ", "Text")]
    [InlineData("نص", "")]
    public void Both_languages_are_required(string? arabic, string? english) =>
        Assert.Equal(CommonErrors.TextRequired, LocalizedText.Create(arabic, english, 50).Error);

    [Fact]
    public void Text_longer_than_the_limit_is_rejected() =>
        Assert.Equal(CommonErrors.TextTooLong, LocalizedText.Create("نص", new string('a', 11), 10).Error);

    [Fact]
    public void Arabic_text_must_contain_arabic_script() =>
        Assert.Equal(CommonErrors.ArabicTextNotArabic, LocalizedText.Create("Text", "Text", 50).Error);

    [Fact]
    public void Equal_texts_are_equal_values() =>
        Assert.Equal(LocalizedText.Create("نص", "Text", 50).Value, LocalizedText.Create("نص", "Text", 50).Value);

    [Fact]
    public void Fits_within_checks_both_languages()
    {
        var text = LocalizedText.Create("نص", "Twelve chars", 50).Value;

        Assert.True(text.FitsWithin(12));
        Assert.False(text.FitsWithin(11));
    }

    [Fact]
    public void Draft_text_may_lack_arabic_and_is_then_incomplete()
    {
        var text = LocalizedText.CreateDraft(null, " Text ", 50).Value;

        Assert.Null(text.Arabic);
        Assert.Equal("Text", text.English);
        Assert.False(text.IsComplete);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Draft_text_treats_blank_arabic_as_missing(string? arabic) =>
        Assert.Null(LocalizedText.CreateDraft(arabic, "Text", 50).Value.Arabic);

    [Fact]
    public void Draft_text_still_requires_english() =>
        Assert.Equal(CommonErrors.TextRequired, LocalizedText.CreateDraft("نص", " ", 50).Error);

    [Fact]
    public void Draft_text_with_arabic_must_use_arabic_script() =>
        Assert.Equal(CommonErrors.ArabicTextNotArabic, LocalizedText.CreateDraft("Text", "Text", 50).Error);

    [Fact]
    public void Bilingual_text_is_complete() =>
        Assert.True(LocalizedText.Create("نص", "Text", 50).Value.IsComplete);

    [Fact]
    public void Fits_within_ignores_missing_arabic() =>
        Assert.True(LocalizedText.CreateDraft(null, "Text", 50).Value.FitsWithin(4));
}
