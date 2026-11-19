using FamilyResilience.Application.Assistant;

namespace FamilyResilience.UnitTests.Application.Assistant;

public sealed class AssistantRequestValidatorTests
{
    private static readonly AssistantReplyRequestValidator Validator = new();

    private static AssistantMessageRequest User(string? text) => new(AssistantMessageRole.User, text);

    private static AssistantMessageRequest Reply(string text) => new(AssistantMessageRole.Assistant, text);

    private static IReadOnlyList<string> Errors(AssistantReplyRequest request) =>
        Validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct().ToList();

    [Fact]
    public void A_chat_ending_with_the_persons_message_is_valid()
    {
        Assert.Empty(Errors(new AssistantReplyRequest("en", [User("Hello"), Reply("Hi"), User("How are we doing?")])));
        Assert.Empty(Errors(new AssistantReplyRequest("ar", [User(new string('a', AssistantReplyRequestValidator.MaxMessageLength))])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("EN")]
    public void The_language_must_be_en_or_ar(string? language) =>
        Assert.Contains("Language", Errors(new AssistantReplyRequest(language, [User("Hello")])));

    [Fact]
    public void There_must_be_one_to_forty_messages()
    {
        Assert.Contains("Messages", Errors(new AssistantReplyRequest("en", null)));
        Assert.Contains("Messages", Errors(new AssistantReplyRequest("en", [])));
        var tooMany = Enumerable.Range(0, AssistantReplyRequestValidator.MaxMessages + 1).Select(_ => User("Hi")).ToList();
        Assert.Contains("messages", Errors(new AssistantReplyRequest("en", tooMany)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void Each_message_needs_text(string? text) =>
        Assert.Contains("Messages[0].Text", Errors(new AssistantReplyRequest("en", [User(text)])));

    [Fact]
    public void A_message_over_four_thousand_characters_after_trimming_is_refused()
    {
        var padded = "  " + new string('a', AssistantReplyRequestValidator.MaxMessageLength) + "  ";
        Assert.Empty(Errors(new AssistantReplyRequest("en", [User(padded)])));
        Assert.Contains(
            "Messages[0].Text",
            Errors(new AssistantReplyRequest("en", [User(new string('a', AssistantReplyRequestValidator.MaxMessageLength + 1))])));
    }

    [Fact]
    public void The_messages_may_hold_twenty_thousand_characters_in_all()
    {
        var full = Enumerable.Range(0, 5).Select(_ => User(new string('a', 4_000))).ToList();
        var over = Enumerable.Range(0, 5).Select(_ => User(new string('a', 4_000))).Append(User("a")).ToList();

        Assert.Empty(Errors(new AssistantReplyRequest("en", full)));
        Assert.Contains("Messages", Errors(new AssistantReplyRequest("en", over)));
    }

    [Fact]
    public void The_last_message_must_be_the_persons_and_roles_must_be_known()
    {
        Assert.Contains("Messages", Errors(new AssistantReplyRequest("en", [User("Hello"), Reply("Hi")])));
        Assert.Contains("Messages[0].Role", Errors(new AssistantReplyRequest("en", [new AssistantMessageRequest(0, "Hello"), User("Hi")])));
    }

    [Fact]
    public void A_memory_may_hold_two_thousand_characters_after_trimming_and_channels_must_be_known()
    {
        var padded = "  " + new string('m', AssistantMemory.MaxLength) + "  ";

        Assert.Empty(Errors(new AssistantReplyRequest("en", [User("Hi")], padded)));
        Assert.Contains("Memory", Errors(new AssistantReplyRequest("en", [User("Hi")], new string('m', AssistantMemory.MaxLength + 1))));
        Assert.Empty(Errors(new AssistantReplyRequest("en", [new AssistantMessageRequest(AssistantMessageRole.User, "Hi", AssistantMessageChannel.Voice)])));
        Assert.Contains(
            "Messages[0].Channel",
            Errors(new AssistantReplyRequest("en", [new AssistantMessageRequest(AssistantMessageRole.User, "Hi", (AssistantMessageChannel)9)])));
    }

    private static IReadOnlyList<string> StartErrors(ConsultantStartMessage start) =>
        new ConsultantStartMessageValidator().Validate(start).Errors.Select(error => error.PropertyName).Distinct().ToList();

    [Fact]
    public void A_live_start_may_carry_no_messages_or_end_with_either_role_within_the_chat_limits()
    {
        Assert.Empty(StartErrors(new ConsultantStartMessage("start", null, null)));
        Assert.Empty(StartErrors(new ConsultantStartMessage("start", "memory", [User("Hello"), Reply("Hi")])));
        Assert.Contains("Type", StartErrors(new ConsultantStartMessage("begin", null, null)));
        Assert.Contains("Type", StartErrors(new ConsultantStartMessage(null, null, null)));
        Assert.Contains("Memory", StartErrors(new ConsultantStartMessage("start", new string('m', AssistantMemory.MaxLength + 1), null)));
        Assert.Contains("Messages[0].Text", StartErrors(new ConsultantStartMessage("start", null, [User(" ")])));
        Assert.Contains(
            "messages",
            StartErrors(new ConsultantStartMessage("start", null, Enumerable.Range(0, AssistantReplyRequestValidator.MaxMessages + 1).Select(_ => User("Hi")).ToList())));
        Assert.Contains(
            "Messages",
            StartErrors(new ConsultantStartMessage("start", null, Enumerable.Range(0, 6).Select(_ => User(new string('a', 4_000))).ToList())));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("ar", true)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void The_live_consultant_speaks_english_or_arabic(string? language, bool valid) =>
        Assert.Equal(valid, new ConsultantStartRequestValidator().Validate(new ConsultantStartRequest { Language = language }).IsValid);
}
