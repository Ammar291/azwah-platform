using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.UnitTests.Domain.Safeguarding;

public sealed class CrisisSignalScreenTests
{
    private static readonly CrisisSignalScreen Screen = new(CrisisSignalCatalog.Load());

    [Fact]
    public void Screen_matches_english_suicide_intent()
    {
        var match = Screen.Screen("Honestly I want to kill myself tonight");

        Assert.Equal(new CrisisSignalMatch("suicide.intent", SafetyConcern.SuicideOrSelfHarm), match);
    }

    [Theory]
    [InlineData("وأنتحر", "suicide.intent")]
    [InlineData("فابي اموت", "suicide.wish_to_die")]
    [InlineData("أُرِيدُ أنْ أَمُوت", "suicide.wish_to_die")]
    [InlineData("راح أنتحر الليلة", "suicide.intent")]
    [InlineData("هددني بالقتل", "danger.threatened")]
    public void Screen_matches_arabic_with_conjunction_prefix_and_variants(string text, string rule)
    {
        Assert.Equal(rule, Screen.Screen(text)?.RuleCode);
    }

    [Fact]
    public void Screen_does_not_strip_a_prefix_when_fewer_than_three_letters_remain()
    {
        var screen = new CrisisSignalScreen(new CrisisSignalList("test", [
            new CrisisSignalRule("t.short", SafetyConcern.AbuseOrDanger, ["اب"]),
            new CrisisSignalRule("t.long", SafetyConcern.HarmToOthers, ["ابي"]),
        ]));

        Assert.Null(screen.Screen("فاب"));
        Assert.Equal("t.long", screen.Screen("فابي")?.RuleCode);
    }

    [Theory]
    [InlineData("بابي", "t.word")]
    [InlineData("سابي", "t.word")]
    [InlineData("وسابي", "t.word")]
    [InlineData("فبابي", "t.word")]
    [InlineData("وابي", "t.word")]
    public void Screen_strips_one_clitic_or_a_conjunction_and_a_clitic_from_the_first_arabic_word(string text, string rule)
    {
        var screen = new CrisisSignalScreen(new CrisisSignalList("test", [new CrisisSignalRule("t.word", SafetyConcern.AbuseOrDanger, ["ابي"])]));

        Assert.Equal(rule, screen.Screen(text)?.RuleCode);
    }

    [Theory]
    [InlineData("باب")]
    [InlineData("ساب")]
    [InlineData("وساب")]
    [InlineData("بسابي")]
    [InlineData("سوابي")]
    public void Screen_strips_no_prefix_that_leaves_fewer_than_three_letters_or_is_out_of_order(string text)
    {
        var screen = new CrisisSignalScreen(new CrisisSignalList("test", [
            new CrisisSignalRule("t.short", SafetyConcern.AbuseOrDanger, ["اب"]),
            new CrisisSignalRule("t.word", SafetyConcern.HarmToOthers, ["ابي"]),
        ]));

        Assert.Null(screen.Screen(text));
    }

    [Theory]
    [InlineData("I can't take it anymore. I keep thinking about ending my life.", "suicide.intent")]
    [InlineData("Honestly, I've been thinking about hurting myself.", "self_harm.act")]
    [InlineData("I keep thinking about killing myself", "suicide.intent")]
    [InlineData("some days I think about ending my life", "suicide.intent")]
    [InlineData("I just want to end it all", "suicide.intent")]
    [InlineData("I'm gonna end it all tonight", "suicide.intent")]
    [InlineData("I want to hang myself", "suicide.intent")]
    [InlineData("I have nothing to live for", "suicide.wish_to_die")]
    [InlineData("i wanna die", "suicide.wish_to_die")]
    [InlineData("I have been self harming again", "self_harm.act")]
    [InlineData("I think about self harm", "self_harm.act")]
    [InlineData("my self-harm is back", "self_harm.act")]
    [InlineData("I keep hurting myself", "self_harm.act")]
    [InlineData("I don't feel safe at home", "danger.threatened")]
    [InlineData("I do not feel safe at home", "danger.threatened")]
    [InlineData("my husband hit me last night", "abuse.being_hurt")]
    [InlineData("he hit me in front of the kids", "abuse.being_hurt")]
    [InlineData("he beat me again", "abuse.being_hurt")]
    [InlineData("I am being abused", "abuse.being_hurt")]
    [InlineData("she hurts me when nobody is home", "abuse.being_hurt")]
    [InlineData("I am scared I will hurt my child", "harm_others.children")]
    [InlineData("I might hurt my son", "harm_others.children")]
    [InlineData("I could hurt my daughter", "harm_others.children")]
    [InlineData("I feel like I might hurt someone", "harm_others.intent")]
    public void Screen_matches_other_english_inflections(string text, string rule)
    {
        Assert.Equal(rule, Screen.Screen(text)?.RuleCode);
    }

    public static TheoryData<string> Relatives => new()
    {
        "husband", "wife", "partner", "father", "mother", "brother", "dad", "mum", "mom", "son", "daughter", "sister", "uncle",
        "aunt", "stepdad", "stepfather", "stepmother", "grandfather", "grandmother", "cousin", "boyfriend", "girlfriend",
        "fiance", "ex",
    };

    [Theory]
    [MemberData(nameof(Relatives))]
    public void Screen_matches_hit_me_after_every_listed_relative(string relative)
    {
        Assert.Equal("abuse.being_hurt", Screen.Screen($"my {relative} hit me yesterday")?.RuleCode);
    }

    [Theory]
    [InlineData("بقتل نفسي", "suicide.intent")]
    [InlineData("سأقتل نفسي", "suicide.intent")]
    [InlineData("سأنتحر", "suicide.intent")]
    [InlineData("وسأنتحر الليلة", "suicide.intent")]
    [InlineData("ودي اموت", "suicide.wish_to_die")]
    [InlineData("ابغا اموت", "suicide.wish_to_die")]
    [InlineData("ما ابغى اعيش", "suicide.wish_to_die")]
    [InlineData("اتمنى اموت", "suicide.wish_to_die")]
    [InlineData("زوجي ضربني", "abuse.being_hurt")]
    [InlineData("ضربني زوجي البارحة", "abuse.being_hurt")]
    [InlineData("وزوجي ضربني", "abuse.being_hurt")]
    [InlineData("زوجي يؤذيني", "abuse.being_hurt")]
    [InlineData("زوجي بيضربني", "abuse.being_hurt")]
    [InlineData("سيضربني اذا عرف", "abuse.being_hurt")]
    [InlineData("وبيضربني", "abuse.being_hurt")]
    [InlineData("خايفة انه يقتلني", "danger.threatened")]
    [InlineData("خايف انه يقتلني", "danger.threatened")]
    public void Screen_matches_other_arabic_forms_and_prefixes(string text, string rule)
    {
        Assert.Equal(rule, Screen.Screen(text)?.RuleCode);
    }

    [Theory]
    [InlineData("أبوي ضربني")]
    [InlineData("ضربني أبي")]
    [InlineData("والدي ضربني")]
    [InlineData("ضربني أخوي")]
    [InlineData("أخي ضربني")]
    [InlineData("ولدي ضربني")]
    [InlineData("ضربني ابني")]
    [InlineData("عمي ضربني")]
    [InlineData("ضربني خالي")]
    [InlineData("جدي ضربني")]
    [InlineData("خطيبي ضربني")]
    [InlineData("ضربني طليقي")]
    [InlineData("زوج أمي ضربني")]
    [InlineData("زوجتي ضربتني")]
    [InlineData("ضربتني أمي")]
    [InlineData("والدتي ضربتني")]
    [InlineData("أختي ضربتني")]
    [InlineData("ضربتني بنتي")]
    [InlineData("عمتي ضربتني")]
    [InlineData("خالتي ضربتني")]
    [InlineData("ضربتني جدتي")]
    [InlineData("خطيبتي ضربتني")]
    [InlineData("طليقتي ضربتني")]
    [InlineData("زوجة أبوي ضربتني")]
    [InlineData("ضربتني زوجة أبي")]
    [InlineData("مرت أبوي ضربتني")]
    public void Screen_matches_the_past_tense_after_or_before_every_listed_arabic_relative(string text)
    {
        Assert.Equal("abuse.being_hurt", Screen.Screen(text)?.RuleCode);
    }

    [Theory]
    [InlineData("my brother hits me up every day")]
    [InlineData("he hits me up when he needs money")]
    [InlineData("my cousin hit me up last night")]
    public void Screen_ignores_hits_me_up(string text)
    {
        Assert.Null(Screen.Screen(text));
    }

    [Fact]
    public void Screen_drops_only_the_hits_me_up_occurrence()
    {
        Assert.Equal("abuse.being_hurt", Screen.Screen("my brother hits me up every day and my husband beats me")?.RuleCode);
    }

    [Fact]
    public void An_exception_cancels_only_the_occurrences_it_overlaps()
    {
        var screen = new CrisisSignalScreen(new CrisisSignalList("test", [
            new CrisisSignalRule("t.rule", SafetyConcern.AbuseOrDanger, ["hits me"]) { Exceptions = ["hits me up"] },
        ]));

        Assert.Null(screen.Screen("he hits me up"));
        Assert.Equal("t.rule", screen.Screen("he hits me up and he hits me")?.RuleCode);
        Assert.Equal("t.rule", screen.Screen("up he hits me")?.RuleCode);
    }

    [Fact]
    public void Screen_prefers_self_harm_over_harm_to_others()
    {
        var match = Screen.Screen("I will kill him and then kill myself");

        Assert.Equal(SafetyConcern.SuicideOrSelfHarm, match?.Concern);
    }

    [Fact]
    public void Screen_prefers_abuse_over_harm_to_others()
    {
        var match = Screen.Screen("he hits me and I want to kill him");

        Assert.Equal(SafetyConcern.AbuseOrDanger, match?.Concern);
    }

    [Fact]
    public void Screen_matches_abuse()
    {
        Assert.Equal("abuse.being_hurt", Screen.Screen("my husband hits me every night")?.RuleCode);
    }

    [Theory]
    [InlineData("my dad hits me")]
    [InlineData("my mum hits me")]
    [InlineData("my son hits me")]
    [InlineData("my uncle beats me")]
    [InlineData("my stepfather hits me")]
    [InlineData("my ex hits me when he is angry")]
    [InlineData("my sister beats me")]
    public void Screen_matches_abuse_by_any_relative(string text)
    {
        Assert.Equal("abuse.being_hurt", Screen.Screen(text)?.RuleCode);
    }

    [Fact]
    public void Screen_flags_topic_words_for_human_review()
    {
        // A known, accepted match: topic words are flagged and a human reviews the alert (open question 58).
        Assert.Equal("suicide.mention", Screen.Screen("the suicide squad film")?.RuleCode);
    }

    [Theory]
    [InlineData("this traffic is killing me")]
    [InlineData("I could kill for a coffee")]
    [InlineData("dead tired")]
    [InlineData("killer workout")]
    [InlineData("that hits me hard")]
    [InlineData("beats me, no idea")]
    [InlineData("my mum is going to kill me when she sees this")]
    [InlineData("a caffeine overdose")]
    [InlineData("الحر يعذبني")]
    [InlineData("اخوي بيقتلني")]
    [InlineData("وبيقتلني اخوي")]
    [InlineData("سامي ضربني بالكرة")]
    [InlineData("")]
    [InlineData(null)]
    public void Screen_ignores_idioms(string? text)
    {
        Assert.Null(Screen.Screen(text));
    }

    [Theory]
    [InlineData("I want to die laughing", "suicide.wish_to_die")]
    [InlineData("kill them with kindness", "harm_others.intent")]
    [InlineData("تعبت من الحياة", "suicide.wish_to_die")]
    [InlineData("I am killing myself at the gym", "suicide.intent")]
    [InlineData("she hit me with the news", "abuse.being_hurt")]
    public void Screen_flags_ambiguous_phrases_for_human_review(string text, string rule)
    {
        // Accepted matches: a human reviews every alert, and open question 58 owns the clinical review of the list.
        Assert.Equal(rule, Screen.Screen(text)?.RuleCode);
    }

    public static TheoryData<string, string> ListedPhrases()
    {
        var data = new TheoryData<string, string>();
        foreach (var rule in CrisisSignalCatalog.Load().Rules)
        {
            foreach (var phrase in rule.Phrases)
            {
                data.Add(rule.Code, phrase);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ListedPhrases))]
    public void Every_listed_phrase_matches_its_own_rule_or_a_higher_priority_one(string ruleCode, string phrase)
    {
        var rule = CrisisSignalCatalog.Load().Rules.Single(r => r.Code == ruleCode);

        var match = Screen.Screen(phrase);

        Assert.NotNull(match);
        Assert.True(match.RuleCode == ruleCode || PriorityOf(match.Concern) < PriorityOf(rule.Concern));
    }

    private static int PriorityOf(SafetyConcern concern) => concern switch
    {
        SafetyConcern.SuicideOrSelfHarm => 0,
        SafetyConcern.AbuseOrDanger => 1,
        _ => 2,
    };

    [Fact]
    public void Screen_matches_whole_tokens_only()
    {
        Assert.Null(Screen.Screen("skill myself up"));
    }

    [Fact]
    public void Screen_reports_the_list_version()
    {
        Assert.Equal("crisis-signals.v1", Screen.Version);
    }

    [Fact]
    public void Screen_returns_the_first_rule_in_file_order_within_a_concern()
    {
        var list = new CrisisSignalList("test", [
            new CrisisSignalRule("a.first", SafetyConcern.AbuseOrDanger, ["beats me"]),
            new CrisisSignalRule("a.second", SafetyConcern.AbuseOrDanger, ["he beats me"]),
        ]);

        Assert.Equal("a.first", new CrisisSignalScreen(list).Screen("he beats me")?.RuleCode);
    }
}
