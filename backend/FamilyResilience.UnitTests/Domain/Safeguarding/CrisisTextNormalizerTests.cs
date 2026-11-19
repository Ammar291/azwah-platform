using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.UnitTests.Domain.Safeguarding;

public sealed class CrisisTextNormalizerTests
{
    [Fact]
    public void Normalize_folds_arabic_variants_and_diacritics()
    {
        Assert.Equal(CrisisTextNormalizer.Normalize("اريد ان اموت"), CrisisTextNormalizer.Normalize("أُرِيدُ أنْ أَمُوت"));
        Assert.Equal("انتحار", CrisisTextNormalizer.Normalize("ــانتحـــار"));
        Assert.Equal("مدرسه", CrisisTextNormalizer.Normalize("مدرسة"));
        Assert.Equal("3", CrisisTextNormalizer.Normalize("٣"));
    }

    [Fact]
    public void Normalize_folds_the_remaining_arabic_letter_variants()
    {
        Assert.Equal("ي و ا ا ا", CrisisTextNormalizer.Normalize("ى ؤ إ آ ٱ"));
        Assert.Equal("ي", CrisisTextNormalizer.Normalize("ئ"));
    }

    [Fact]
    public void Normalize_drops_joiners_and_quranic_marks_without_splitting_words()
    {
        Assert.Equal("انتحر", CrisisTextNormalizer.Normalize("ان‌ت‍حر"));
        Assert.Equal("انتحر", CrisisTextNormalizer.Normalize("انتۖحرۭ"));
    }

    [Fact]
    public void Normalize_drops_apostrophes_and_punctuation()
    {
        Assert.Equal("i dont want to live", CrisisTextNormalizer.Normalize("I DON’T want to live!!!"));
        Assert.Equal("dont", CrisisTextNormalizer.Normalize("don't"));
    }

    [Fact]
    public void Normalize_collapses_whitespace_and_handles_missing_text()
    {
        Assert.Equal("a b", CrisisTextNormalizer.Normalize("  a \t\n b  "));
        Assert.Equal(string.Empty, CrisisTextNormalizer.Normalize(null));
        Assert.Equal(string.Empty, CrisisTextNormalizer.Normalize("!!!"));
    }
}
