using FamilyResilience.Application.Assistant;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>The output guard (D-126): the app shows plain text, so Markdown never reaches it. Texts are synthetic.</summary>
public sealed class AssistantReplyGuardTests
{
    [Theory]
    [InlineData("You can contact **Household budget and debt advice**.", "You can contact Household budget and debt advice.")]
    [InlineData("__Strong__ and *gentle* words", "Strong and gentle words")]
    [InlineData("### Next steps\n1. Talk\n2. Plan", "Next steps\n1. Talk\n2. Plan")]
    [InlineData("* one\n+ two\n• three\n- four", "- one\n- two\n- three\n- four")]
    [InlineData("See [the guide](https://example.org/guide).", "See the guide (https://example.org/guide).")]
    [InlineData("Use `this` word", "Use this word")]
    [InlineData("Line one  \n\n\n\nLine two\n---\n", "Line one\n\nLine two")]
    [InlineData("\r\nWindows\r\nlines\r\n", "Windows\nlines")]
    [InlineData("5 * 3 = 15 and 2*x", "5 * 3 = 15 and 2*x")]
    [InlineData("member_1 and member_2", "member_1 and member_2")]
    [InlineData("**عزوة** معكم", "عزوة معكم")]
    public void Markdown_becomes_plain_text_without_changing_the_words(string reply, string expected) =>
        Assert.Equal(expected, AssistantReplyGuard.Clean(reply));

    [Fact]
    public void A_reply_is_cut_at_the_limit()
    {
        var cleaned = AssistantReplyGuard.Clean(new string('a', AssistantReplyGuard.MaxLength + 50));

        Assert.Equal(AssistantReplyGuard.MaxLength, cleaned.Length);
    }

    [Fact]
    public void A_memory_from_the_model_is_cleaned_and_capped_and_nothing_becomes_null()
    {
        Assert.Null(AssistantMemory.FromModel("  ** **  "));
        Assert.Equal("- Fact", AssistantMemory.FromModel("* **Fact**"));
        Assert.Equal(AssistantMemory.MaxLength, AssistantMemory.FromModel(new string('b', 3_000))?.Length);
        Assert.Null(AssistantMemory.Normalise("   "));
    }
}
