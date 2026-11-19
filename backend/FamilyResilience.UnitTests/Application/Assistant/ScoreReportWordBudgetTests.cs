using FamilyResilience.Application.Assistant;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>
/// The report's word budgets (D-145): asked for in the prompt and enforced after the model answers, at a sentence boundary
/// where there is one, because a model's word count cannot be trusted.
/// </summary>
public sealed class ScoreReportWordBudgetTests
{
    private static string Words(int count, string word = "word") => string.Join(' ', Enumerable.Repeat(word, count));

    [Fact]
    public void Text_within_its_budget_is_kept_as_written()
    {
        Assert.Equal("Your family talks often. It plans together.", ScoreReportWordBudget.Trim("  Your family talks often. It plans together. ", 7));
    }

    [Fact]
    public void Longer_text_ends_at_the_last_sentence_that_fits()
    {
        Assert.Equal(
            "One two three. Four five six!",
            ScoreReportWordBudget.Trim("One two three. Four five six! Seven eight nine ten.", 7));
    }

    [Fact]
    public void Arabic_sentences_end_where_their_own_marks_say()
    {
        Assert.Equal("أسرتكم قوية؟ نعم.", ScoreReportWordBudget.Trim("أسرتكم قوية؟ نعم. وتحتاج إلى وقت أطول معاً.", 4));
    }

    [Fact]
    public void A_first_sentence_longer_than_the_budget_is_cut_at_the_budget_and_marked()
    {
        Assert.Equal($"{Words(5)}…", ScoreReportWordBudget.Trim(Words(9) + ".", 5));
    }

    [Fact]
    public void A_part_keeps_one_of_each_list_two_steps_and_its_budgets()
    {
        ScoreReportPoint[] points = [new("One " + Words(10), Words(40) + "."), new("Two", "D"), new("Three", "D")];
        ScoreReportImprovement[] improvements =
        [
            new("One", Words(30) + ".", [Words(30) + ".", "Step two.", "Step three."]) { SupportCodes = ["a", "b"] },
            new("Two", "R", ["S"]),
        ];

        var limited = new ScoreReportPartWriting(points, points, improvements).Limited();

        Assert.True(limited.IsUsable());
        var strength = Assert.Single(limited.Strengths);
        Assert.Equal(ScoreReportWordBudget.TitleWords, ScoreReportWordBudget.Count(strength.Title));
        Assert.Equal(ScoreReportWordBudget.DetailWords, ScoreReportWordBudget.Count(strength.Detail.TrimEnd('…')));
        Assert.Single(limited.Weaknesses);
        var improvement = Assert.Single(limited.Improvements);
        Assert.Equal(ScoreReportWordBudget.MaxSteps, improvement.Steps.Count);
        Assert.Equal(ScoreReportWordBudget.StepWords, ScoreReportWordBudget.Count(improvement.Steps[0].TrimEnd('…')));
        Assert.Equal(ScoreReportWordBudget.ReasonWords, ScoreReportWordBudget.Count(improvement.Reason.TrimEnd('…')));
        Assert.Equal(["a", "b"], improvement.SupportCodes);
    }

    [Fact]
    public void A_whole_report_keeps_its_summary_budget_and_two_strengths_and_two_weaknesses()
    {
        var sentence = Words(9) + ".";
        ScoreReportPoint[] points = [new("A", "D"), new("B", "D"), new("C", "D")];
        var writing = new ScoreReportWriting(
            string.Join(' ', Enumerable.Repeat(sentence, 6)),
            points,
            points,
            [new("A", "R", ["S", "T", "U"]) { SectionCode = "a" }, new("B", "R", ["S"]), new("C", "R", ["S"]), new("D", "R", ["S"])]);

        var limited = writing.Limited();

        Assert.True(limited.IsUsable());
        Assert.Equal(string.Join(' ', Enumerable.Repeat(sentence, 3)), limited.Summary);
        Assert.Equal(["A", "B"], limited.Strengths.Select(point => point.Title));
        Assert.Equal(["A", "B"], limited.Weaknesses.Select(point => point.Title));
        // Every improvement stays until the steps are ordered by where their areas stand; then only the weakest are kept.
        Assert.Equal(4, limited.Improvements.Count);
        Assert.Equal(["S", "T"], limited.Improvements[0].Steps);
        Assert.Equal("a", limited.Improvements[0].SectionCode);
    }

    [Fact]
    public void Every_prompt_states_its_word_budgets()
    {
        Assert.Contains($"at most {ScoreReportWordBudget.SummaryWords} words", ScoreReportPrompt.SummaryInstructions("en"), StringComparison.Ordinal);
        Assert.Contains($"at most {ScoreReportWordBudget.SummaryWords} words", ScoreReportPrompt.Instructions("en"), StringComparison.Ordinal);
        foreach (var instructions in new[] { ScoreReportPrompt.Instructions("en"), ScoreReportPrompt.PartInstructions("en") })
        {
            Assert.Contains($"at most {ScoreReportWordBudget.DetailWords} words", instructions, StringComparison.Ordinal);
            Assert.Contains($"at most {ScoreReportWordBudget.StepWords} words", instructions, StringComparison.Ordinal);
            Assert.Contains($"at most {ScoreReportWordBudget.ReasonWords} words", instructions, StringComparison.Ordinal);
        }
    }
}
