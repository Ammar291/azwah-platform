using FamilyResilience.Application.SupportResources;

namespace FamilyResilience.UnitTests.Application.SupportResources;

public sealed class SupportResourceQueryTextTests
{
    private static SearchTerm Arabic(string text) => new(text, SearchTermKind.Arabic);

    [Fact]
    public void English_words_become_lowercase_terms() =>
        Assert.Equal(
            [new SearchTerm("debt", SearchTermKind.English), new SearchTerm("advice", SearchTermKind.English)],
            SupportResourceQueryText.Parse("Debt Advice!"));

    [Fact]
    public void Digits_and_symbols_never_become_terms()
    {
        var terms = SupportResourceQueryText.Parse("call 0501234567, ٠٥٠١٢٣ or a@b.ae");

        Assert.NotEmpty(terms);
        Assert.All(terms, term =>
        {
            Assert.DoesNotContain(term.Text, c => char.IsDigit(c) || c is '@' or '.');
        });
    }

    [Theory]
    [InlineData("الديون", "ديون")]
    [InlineData("والديون", "ديون")]
    [InlineData("بالأسرة", "اسره")]
    [InlineData("للأطفال", "اطفال")]
    [InlineData("الى", "الي")]
    [InlineData("مُسَاعَدَة", "مساعده")]
    [InlineData("ٱلتثقيف", "تثقيف")]
    [InlineData("إدارة", "اداره")]
    public void NormaliseArabic_folds_and_strips_the_article(string word, string expected) =>
        Assert.Equal(expected, SupportResourceQueryText.NormaliseArabic(word));

    [Theory]
    [InlineData("ديوني")]
    [InlineData("الديون")]
    [InlineData("والديون")]
    public void Arabic_inflections_reduce_to_the_same_stem(string text)
    {
        var term = Assert.Single(SupportResourceQueryText.Parse(text));

        Assert.Equal(Arabic("ديون"), term);
        Assert.True(term.IsPrefix);
    }

    [Fact]
    public void Arabic_function_words_are_dropped() =>
        Assert.Equal(
            [Arabic("اسدد"), Arabic("ديون"), Arabic("بنك")],
            SupportResourceQueryText.Parse("كيف اسدد ديوني في البنك"));

    // A function word with a pronoun suffix ("علينا", "عندهم") reduces to the function word itself; "علي" would then
    // prefix-match every document with "على" (documents fold ى to ي), so the stem is checked again.
    [Fact]
    public void A_function_word_with_a_suffix_is_dropped_after_the_suffix_is_stripped() =>
        Assert.Equal([Arabic("ديون")], SupportResourceQueryText.Parse("علينا ديون"));

    [Theory]
    [InlineData("علينا")]
    [InlineData("عليهم")]
    [InlineData("عليكم")]
    [InlineData("عندهم")]
    [InlineData("لديكم")]
    public void Function_word_stems_left_by_a_suffix_strip_are_dropped(string word) =>
        Assert.Empty(SupportResourceQueryText.Parse(word));

    [Fact]
    public void A_suffix_is_stripped_only_when_three_letters_remain() =>
        Assert.Equal([Arabic("بنك")], SupportResourceQueryText.Parse("بنك"));

    [Fact]
    public void Mixed_scripts_split_into_separate_terms() =>
        Assert.Equal(
            [new SearchTerm("loan", SearchTermKind.English), Arabic("ديون"), new SearchTerm("help", SearchTermKind.English)],
            SupportResourceQueryText.Parse("loan الديون help"));

    [Fact]
    public void At_most_eight_distinct_terms_are_kept()
    {
        var terms = SupportResourceQueryText.Parse("aa bb cc dd ee ff gg hh ii jj aa");

        Assert.Equal(8, terms.Count);
        Assert.Equal("aa", terms[0].Text);
        Assert.Equal("hh", terms[7].Text);
    }

    [Fact]
    public void Repeated_terms_count_once() =>
        Assert.Single(SupportResourceQueryText.Parse("Debt debt DEBT"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123 !!")]
    public void Blank_text_has_no_terms(string? text) =>
        Assert.Empty(SupportResourceQueryText.Parse(text));

    // Tuned from the synthetic evaluation (Task 9): English stop words used to fill the eight-term budget, so the words
    // that mattered at the end of a longer question were cut ("... about his phone and friends" lost both).
    [Fact]
    public void English_stop_words_do_not_use_up_the_term_budget() =>
        Assert.Equal(
            ["constant", "arguments", "teenage", "son", "phone", "friends"],
            SupportResourceQueryText.Parse("constant arguments with my teenage son about his phone and friends")
                .Select(term => term.Text));

    [Theory]
    [InlineData("the")]
    [InlineData("And")]
    [InlineData("about")]
    [InlineData("should")]
    [InlineData("don")]
    public void English_stop_words_never_become_terms(string word) =>
        Assert.Empty(SupportResourceQueryText.Parse(word));

    // Tuned from the synthetic evaluation (Task 9): words that say a thing exists, or how much or how often, carry no
    // search meaning but counted towards the half of the terms a resource must contain.
    [Theory]
    [InlineData("توجد")]
    [InlineData("يوجد")]
    [InlineData("هناك")]
    [InlineData("كثير")]
    [InlineData("كثيراً")]
    [InlineData("جدًا")]
    [InlineData("أكثر")]
    [InlineData("أقل")]
    [InlineData("فقط")]
    [InlineData("دائماً")]
    [InlineData("بسبب")]
    [InlineData("لأن")]
    [InlineData("لأنه")]
    public void Arabic_existence_quantity_frequency_and_cause_words_are_dropped(string word) =>
        Assert.Empty(SupportResourceQueryText.Parse(word));

    // Tuned from the synthetic evaluation (Task 9): a function word with "and" attached ("وأنا", "وما") is still a
    // function word.
    [Theory]
    [InlineData("وأنا")]
    [InlineData("وأنت")]
    [InlineData("وما")]
    [InlineData("ولا")]
    [InlineData("وهو")]
    [InlineData("وهي")]
    [InlineData("وفي")]
    [InlineData("ومن")]
    [InlineData("وإذا")]
    [InlineData("ولكن")]
    public void Arabic_function_words_with_an_attached_and_are_dropped(string word) =>
        Assert.Empty(SupportResourceQueryText.Parse(word));

    // Tuned from the synthetic evaluation (Task 9): common Gulf function words, alongside ابي, ابغى and وش.
    [Theory]
    [InlineData("وايد")]
    [InlineData("دايم")]
    [InlineData("دايماً")]
    [InlineData("بس")]
    [InlineData("عشان")]
    [InlineData("علشان")]
    [InlineData("وين")]
    [InlineData("ليش")]
    [InlineData("شنو")]
    [InlineData("مب")]
    [InlineData("مو")]
    public void Gulf_function_words_are_dropped(string word) =>
        Assert.Empty(SupportResourceQueryText.Parse(word));

    [Fact]
    public void Only_the_words_that_carry_meaning_remain_in_a_gulf_question() =>
        Assert.Equal(
            [Arabic("ارهاق"), Arabic("وضغط"), Arabic("شغل"), Arabic("دوام")],
            SupportResourceQueryText.Parse("إرهاق وضغط شغل كثير في الدوام"));
}
