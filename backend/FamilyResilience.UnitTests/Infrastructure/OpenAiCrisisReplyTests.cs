using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Infrastructure.Assistant;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>The consultant's supportive reply on the Responses API (D-141), against a fake transport. Every text is synthetic.</summary>
public sealed class OpenAiCrisisReplyTests
{
    private const string Completed =
        """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"  Synthetic supportive words.  "}]}]}""";

    private static (OpenAiCrisisReply Reply, FakeOpenAiTransport Transport, CapturedLogs<OpenAiCrisisReply> Logs) Create(
        HttpStatusCode status, string body, Func<string, string?>? more = null)
    {
        var transport = new FakeOpenAiTransport(status, body);
        var logs = new CapturedLogs<OpenAiCrisisReply>();
        var settings = OpenAiSettings.From(key => key == OpenAiSettings.ApiKeySetting ? "sk-synthetic" : more?.Invoke(key));
        return (new OpenAiCrisisReply(settings, new HttpClient(transport), logs), transport, logs);
    }

    [Fact]
    public async Task It_asks_the_crisis_model_without_reasoning_with_the_instructions_and_the_persons_words_as_data()
    {
        var (reply, transport, _) = Create(HttpStatusCode.OK, Completed);

        var words = await reply.WriteAsync(AssistantLanguages.Arabic, "zqx synthetic words", TestContext.Current.CancellationToken);

        Assert.Equal("Synthetic supportive words.", words);
        var sent = Assert.Single(transport.Requests);
        Assert.Equal(("https://api.openai.com/v1/responses", "Bearer sk-synthetic"), (sent.Uri, sent.Authorization));
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        Assert.Equal((OpenAiSettings.DefaultCrisisModel, "none", false), (root.GetProperty("model").GetString(), root.GetProperty("reasoning").GetProperty("effort").GetString(), root.GetProperty("store").GetBoolean()));
        Assert.Equal(AssistantPrompt.CrisisReplyInstructions(AssistantLanguages.Arabic), root.GetProperty("instructions").GetString());
        var input = Assert.Single(root.GetProperty("input").EnumerateArray());
        Assert.Equal(("user", AssistantPrompt.CrisisReplyInput("zqx synthetic words")), (input.GetProperty("role").GetString(), input.GetProperty("content").GetString()));
    }

    [Fact]
    public async Task Its_model_and_effort_are_settings()
    {
        var (reply, transport, _) = Create(HttpStatusCode.OK, Completed, key => key switch
        {
            OpenAiSettings.CrisisModelSetting => " another-model ",
            OpenAiSettings.CrisisReasoningEffortSetting => "low",
            _ => null,
        });

        await reply.WriteAsync(AssistantLanguages.English, "zqx", TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(transport.Requests).Body);
        Assert.Equal(("another-model", "low"), (body.RootElement.GetProperty("model").GetString(), body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"refusal","refusal":"zqx"}]}]}""", "refused")]
    [InlineData(HttpStatusCode.OK, """{"status":"incomplete","output":[]}""", "not_completed")]
    [InlineData(HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"  "}]}]}""", "no_text")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"message":"zqx provider text"}}""", "status_429")]
    public async Task Anything_but_written_words_is_null_with_a_fixed_code_only(HttpStatusCode status, string answer, string code)
    {
        var (reply, transport, logs) = Create(status, answer);

        var words = await reply.WriteAsync(AssistantLanguages.English, "zqx synthetic words", TestContext.Current.CancellationToken);

        Assert.Null(words);
        Assert.Single(transport.Requests);
        var line = Assert.Single(logs.Lines);
        Assert.Contains(code, line, StringComparison.Ordinal);
        Assert.DoesNotContain("zqx", line, StringComparison.Ordinal);
    }

    [Fact]
    public void It_needs_the_key()
    {
        var settings = OpenAiSettings.From(_ => null);

        Assert.False(new OpenAiCrisisReply(settings, new HttpClient(), new CapturedLogs<OpenAiCrisisReply>()).IsConfigured);
    }
}
