using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.UnitTests.Application.Safeguarding;

public sealed class CrisisSignalCatalogTests
{
    [Fact]
    public void Catalog_loads_the_embedded_v1_list()
    {
        var list = CrisisSignalCatalog.Load();

        Assert.Equal("crisis-signals.v1", list.Version);
        Assert.Contains(list.Rules, r => r.Concern == SafetyConcern.SuicideOrSelfHarm);
        Assert.Contains(list.Rules, r => r.Concern == SafetyConcern.HarmToOthers);
        Assert.Contains(list.Rules, r => r.Concern == SafetyConcern.AbuseOrDanger);
        Assert.DoesNotContain(list.Rules, r => r.Concern == SafetyConcern.LowResilienceScore);
        Assert.Equal(list.Rules.Count, list.Rules.Select(r => r.Code).Distinct().Count());
        var phrases = list.Rules.SelectMany(r => r.Phrases).ToList();
        Assert.Equal(phrases.Count, phrases.Distinct().Count());
        Assert.All(phrases, p => Assert.Equal(CrisisTextNormalizer.Normalize(p), p));
    }

    [Fact]
    public void Catalog_has_at_least_six_phrases_per_language_for_the_suicide_rules()
    {
        var list = CrisisSignalCatalog.Load();
        var suicide = list.Rules
            .Where(r => r.Concern == SafetyConcern.SuicideOrSelfHarm && r.Code.StartsWith("suicide.", StringComparison.Ordinal))
            .SelectMany(r => r.Phrases)
            .ToList();

        Assert.True(suicide.Count(p => p.Any(c => c is >= 'a' and <= 'z')) >= 6);
        Assert.True(suicide.Count(p => p.Any(c => c is >= '؀' and <= 'ۿ')) >= 6);
    }

    private static string Json(string version = "crisis-signals.v1", string rules = "") =>
        $$"""{ "version": "{{version}}", "rules": [ {{rules}} ] }""";

    private static string Rule(string code = "a.b", string concern = "abuse_or_danger", string phrases = """{ "en": ["x y"] }""") =>
        $$"""{ "code": "{{code}}", "concern": "{{concern}}", "phrases": {{phrases}} }""";

    public static TheoryData<string, string> InvalidLists => new()
    {
        { Json(rules: Rule(concern: "bogus")), "unknown_concern" },
        { Json(rules: Rule(concern: "low_resilience_score")), "unknown_concern" },
        { Json(rules: Rule() + "," + Rule()), "duplicate_rule_code" },
        { Json(rules: Rule(phrases: """{ "en": [] }""")), "rule_without_phrases" },
        { Json(rules: Rule(phrases: "{}")), "rule_without_phrases" },
        { Json(rules: Rule(phrases: """{ "en": ["!!!"] }""")), "empty_phrase" },
        { Json(rules: Rule(code: "a.b") + "," + Rule(code: "c.d")), "duplicate_phrase" },
        { Json(rules: Rule(code: "A.b")), "invalid_rule_code" },
        { Json(version: "crisis-signals.v2", rules: Rule()), "unsupported_version" },
        { "{ not json", "malformed" },
        { Json(rules: Rule(phrases: """{ "en": ["x y"] }, "except": { "en": ["?"] }""")), "empty_phrase" },
        { Json(rules: Rule(phrases: """{ "en": ["x y"] }, "except": { "fr": ["x y z"] }""")), "unknown_language" },
        { Json(rules: Rule(phrases: """{ "en": ["x y"] }, "except": ["x y z"]""")), "malformed" },
    };

    [Theory]
    [MemberData(nameof(InvalidLists))]
    public void Catalog_rejects_invalid_lists(string json, string reason)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CrisisSignalCatalog.Parse(json));

        Assert.StartsWith("crisis_signal_list_invalid", ex.Message, StringComparison.Ordinal);
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_normalises_phrases_at_load()
    {
        var list = CrisisSignalCatalog.Parse(Json(rules: Rule(phrases: """{ "ar": ["أُرِيدُ أنْ أَمُوت"], "en": ["Don't Do It"] }""")));

        Assert.Equal(["اريد ان اموت", "dont do it"], list.Rules[0].Phrases);
        Assert.Empty(list.Rules[0].Exceptions);
    }

    [Fact]
    public void Catalog_reads_a_rules_exceptions_normalised()
    {
        var list = CrisisSignalCatalog.Parse(Json(rules: Rule(phrases: """{ "en": ["hits me"] }, "except": { "en": ["Hits Me UP"] }""")));

        Assert.Equal(["hits me up"], list.Rules[0].Exceptions);
    }

    [Fact]
    public void The_abuse_rule_excepts_hits_me_up()
    {
        var rule = CrisisSignalCatalog.Load().Rules.Single(r => r.Code == "abuse.being_hurt");

        Assert.Equal(["hits me up", "hit me up"], rule.Exceptions);
    }
}
