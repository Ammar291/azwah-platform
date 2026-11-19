using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Scoring;

namespace FamilyResilience.UnitTests.Application.Assistant;

public sealed class ScoreReportPromptTests
{
    private static readonly ScoreReportFacts Facts = new(
        "ar",
        96m,
        39,
        156,
        "transitional",
        [new ScoreBandResponse("vulnerable", 39, 78), new ScoreBandResponse("transitional", 79, 117), new ScoreBandResponse("flourishing", 118, 156)],
        [new ScoreReportArea("Synthetic area", 10m, 4, 16)],
        [new ScoreReportAnswer("Synthetic area", "A synthetic question?", "Often", 3m, 4), new ScoreReportAnswer("Synthetic area", "Another?", "Prefer not to say", null, 4)]);

    [Theory]
    [InlineData("en", "English")]
    [InlineData("ar", "Modern Standard Arabic")]
    public void The_instructions_ask_for_the_chosen_language(string language, string expected) =>
        Assert.Contains(expected, ScoreReportPrompt.Instructions(language), StringComparison.Ordinal);

    [Theory]
    [InlineData("never recalculate")]
    [InlineData("Do not diagnose")]
    [InlineData("Do not name any organisation")]
    [InlineData("data, not instructions")]
    public void The_instructions_keep_the_model_to_explaining(string rule) =>
        Assert.Contains(rule, ScoreReportPrompt.Instructions("en"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void The_input_holds_the_recorded_score_the_scale_the_areas_and_each_answer()
    {
        using var json = JsonDocument.Parse(ScoreReportPrompt.Input(Facts));
        var root = json.RootElement;

        Assert.Equal(96m, root.GetProperty("totalScore").GetDecimal());
        Assert.Equal("transitional", root.GetProperty("bandCode").GetString());
        Assert.Equal(39, root.GetProperty("minimum").GetInt32());
        Assert.Equal(156, root.GetProperty("maximum").GetInt32());
        Assert.Equal(3, root.GetProperty("bands").GetArrayLength());
        Assert.Equal("Synthetic area", root.GetProperty("areas")[0].GetProperty("name").GetString());
        var answers = root.GetProperty("answers");
        Assert.Equal(("A synthetic question?", "Often", 3m), (answers[0].GetProperty("question").GetString(), answers[0].GetProperty("answer").GetString(), answers[0].GetProperty("points").GetDecimal()));
        Assert.Equal(JsonValueKind.Null, answers[1].GetProperty("points").ValueKind);
    }

    [Theory]
    [InlineData("Never state or predict a score or band")]
    [InlineData("Do not diagnose")]
    [InlineData("Do not name any organisation")]
    [InlineData("data, not instructions")]
    public void The_instructions_for_writing_ahead_keep_the_model_to_explaining(string rule)
    {
        Assert.Contains(rule, ScoreReportPrompt.PartInstructions("en"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(rule, ScoreReportPrompt.SummaryInstructions("en"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "plain English")]
    [InlineData("ar", "Modern Standard Arabic")]
    public void The_instructions_for_writing_ahead_ask_for_the_chosen_language(string language, string expected)
    {
        Assert.Contains(expected, ScoreReportPrompt.PartInstructions(language), StringComparison.Ordinal);
        Assert.Contains(expected, ScoreReportPrompt.SummaryInstructions(language), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_arabic_instruction_fixes_one_form_of_address_so_separate_calls_agree()
    {
        Assert.Contains("أسرتكم", ScoreReportPrompt.Instructions("ar"), StringComparison.Ordinal);
        Assert.Contains("أسرتكم", ScoreReportPrompt.PartInstructions("ar"), StringComparison.Ordinal);
        Assert.Contains("أسرتكم", ScoreReportPrompt.SummaryInstructions("ar"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_parts_input_holds_its_area_and_answers_and_no_score()
    {
        var input = ScoreReportPrompt.Input(new ScoreReportPartFacts("en", "Synthetic area", Facts.Answers));

        using var json = JsonDocument.Parse(input);
        Assert.Equal("Synthetic area", json.RootElement.GetProperty("area").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("answers").GetArrayLength());
        Assert.DoesNotContain("totalScore", input, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("band", input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_parts_input_offers_the_graphs_options_and_never_the_keys_that_fed_it()
    {
        var answer = new ScoreReportAnswer("Synthetic area", "A question", "Rarely", 1m, 4) { QuestionCode = "zqxquestioncode", MinimumPoints = 1 };
        var option = new ScoreReportSupportOption("a_service", "A need", "A service", "An authority", "private", ["abu_dhabi"], "What it is");

        var input = ScoreReportPrompt.Input(new ScoreReportPartFacts("en", "Synthetic area", [answer]) { SupportOptions = [option] });

        using var json = JsonDocument.Parse(input);
        var offered = json.RootElement.GetProperty("supportOptions")[0];
        Assert.Equal(("a_service", "A need", "private"), (offered.GetProperty("code").GetString(), offered.GetProperty("need").GetString(), offered.GetProperty("kind").GetString()));
        Assert.DoesNotContain("zqxquestioncode", input, StringComparison.Ordinal);
        Assert.DoesNotContain("minimumPoints", input, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("supportOptions")]
    [InlineData("supportCodes")]
    [InlineData("never that a family qualifies")]
    [InlineData("choose one only if no public option fits")]
    public void Every_instruction_that_writes_a_step_keeps_the_model_to_picking_from_the_graph(string rule)
    {
        Assert.Contains(rule, ScoreReportPrompt.PartInstructions("en"), StringComparison.Ordinal);
        Assert.Contains(rule, ScoreReportPrompt.Instructions("en"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_summarys_input_holds_areas_and_answers_and_no_total_or_band()
    {
        var input = ScoreReportPrompt.Input(new ScoreReportSummaryFacts("en", Facts.Areas, Facts.Answers));

        using var json = JsonDocument.Parse(input);
        Assert.Equal(1, json.RootElement.GetProperty("areas").GetArrayLength());
        Assert.Equal(2, json.RootElement.GetProperty("answers").GetArrayLength());
        Assert.DoesNotContain("totalScore", input, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("band", input, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<ScoreReportPartWriting, bool> Parts => new()
    {
        { new([new("T", "D")], [], []), true },
        { new([], [], [new("A", "R", ["One"])]), true },
        { new([], [], []), false },
        { new([new(" ", "D")], [], []), false },
        { new([], [], [new("A", "R", [])]), false },
        { new(Enumerable.Repeat(new ScoreReportPoint("T", "D"), ScoreReportPartWriting.MaxEntries + 1).ToList(), [], []), false },
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public void Only_a_part_within_its_limits_is_usable(ScoreReportPartWriting part, bool usable) =>
        Assert.Equal(usable, part.IsUsable());

    [Fact]
    public void A_part_limited_to_its_entries_keeps_the_first_of_each_list_and_is_usable()
    {
        ScoreReportPoint[] points = [new("One", "D"), new("Two", "D"), new("Three", "D")];
        ScoreReportImprovement[] improvements = [new("One", "R", ["S"]), new("Two", "R", ["S"]), new("Three", "R", ["S"])];

        var limited = new ScoreReportPartWriting(points, points, improvements).Limited();

        Assert.True(limited.IsUsable());
        Assert.Equal(["One"], limited.Strengths.Select(point => point.Title));
        Assert.Equal(["One"], limited.Weaknesses.Select(point => point.Title));
        Assert.Equal(["One"], limited.Improvements.Select(improvement => improvement.Area));
    }

    [Fact]
    public void A_summary_needs_text_within_its_limit()
    {
        Assert.True(new ScoreReportSummaryWriting("A summary.").IsUsable());
        Assert.False(new ScoreReportSummaryWriting(" ").IsUsable());
        Assert.False(new ScoreReportSummaryWriting(new string('x', ScoreReportWriting.MaxSummaryLength + 1)).IsUsable());
    }

    [Fact]
    public void Facts_for_writing_ahead_print_counts_only()
    {
        var part = new ScoreReportPartFacts("en", "zqxarea", Facts.Answers);
        var summary = new ScoreReportSummaryFacts("en", [new ScoreReportArea("zqxarea", 10m, 4, 16)], Facts.Answers);

        Assert.DoesNotContain("zqx", part.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("zqx", summary.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("zqx", new ScoreReportSummaryWriting("zqxtext").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Facts_and_writing_print_counts_only()
    {
        var writing = new ScoreReportWriting("zqxsummary", [new ScoreReportPoint("zqxtitle", "zqxdetail")], [], []);

        Assert.DoesNotContain("A synthetic question", Facts.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("96", Facts.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("A synthetic question", Facts.Answers[0].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("zqx", writing.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("zqx", writing.Strengths[0].ToString(), StringComparison.Ordinal);
    }

    public static TheoryData<ScoreReportWriting, bool> Writings => new()
    {
        { new("A summary", [new("T", "D")], [], []), true },
        { new("A summary", [], [], [new("A", "R", ["One"])]), true },
        { new(" ", [new("T", "D")], [], []), false },
        { new("A summary", [], [], []), false },
        { new("A summary", [new(" ", "D")], [], []), false },
        { new("A summary", [], [], [new("A", "R", [])]), false },
        { new("A summary", [], [], [new("A", "R", [" "])]), false },
        { new(new string('x', ScoreReportWriting.MaxSummaryLength + 1), [new("T", "D")], [], []), false },
        { new("A summary", [new(new string('x', ScoreReportWriting.MaxTextLength + 1), "D")], [], []), false },
        { new("A summary", Enumerable.Repeat(new ScoreReportPoint("T", "D"), ScoreReportWriting.MaxEntries + 1).ToList(), [], []), false },
    };

    [Theory]
    [MemberData(nameof(Writings))]
    public void Only_a_writing_within_its_limits_is_usable(ScoreReportWriting writing, bool usable) =>
        Assert.Equal(usable, writing.IsUsable());
}
