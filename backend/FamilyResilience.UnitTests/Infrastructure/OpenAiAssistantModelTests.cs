using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Infrastructure.Assistant;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>The OpenAI text adapter against a fake transport: no request leaves the test; the key and texts are synthetic.</summary>
public sealed class OpenAiAssistantModelTests
{
    private const string Key = "sk-synthetic-test-key";

    private static readonly AssistantToolDefinition Directory = new(
        "search_support_directory",
        "zqx directory",
        """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"],"additionalProperties":false}""");

    private static AssistantModelStep Step(bool toolsAllowed = true, params AssistantContinuationItem[] continuation) => new(
        "zqx instructions",
        [
            new AssistantInput(AssistantInputKind.Data, "zqx context"),
            new AssistantInput(AssistantInputKind.User, "zqx first question"),
            new AssistantInput(AssistantInputKind.Assistant, "zqx first reply"),
            new AssistantInput(AssistantInputKind.User, "zqx second question"),
        ],
        [Directory],
        continuation,
        toolsAllowed);

    private static string Completed(params string[] texts) => JsonSerializer.Serialize(new
    {
        status = "completed",
        output = new object[]
        {
            new { type = "reasoning", id = "rs_1", encrypted_content = "zqx-sealed" },
            new { type = "message", content = texts.Select(text => new { type = "output_text", text }).ToArray() },
        },
        usage = new { input_tokens = 120, output_tokens = 30 },
    });

    private const string ToolCalls = """
        {"status":"completed","output":[
          {"type":"reasoning","id":"rs_9","encrypted_content":"zqx-sealed"},
          {"type":"function_call","id":"fc_1","call_id":"call_a","name":"search_support_directory","arguments":"{\"text\":\"zqx debt\"}"},
          {"type":"function_call","id":"fc_2","call_id":"call_b","name":"suggest_app_screen","arguments":"{\"screen\":\"services\"}"}],
         "usage":{"input_tokens":80,"output_tokens":12}}
        """;

    private static (OpenAiAssistantModel Model, FakeOpenAiTransport Transport, CapturedLogs<OpenAiAssistantModel> Logs) Create(
        HttpStatusCode status, string body, string? apiKey = Key, Func<string, string?>? more = null)
    {
        var transport = new FakeOpenAiTransport(status, body);
        var logs = new CapturedLogs<OpenAiAssistantModel>();
        var settings = OpenAiSettings.From(key => key == OpenAiSettings.ApiKeySetting ? apiKey : more?.Invoke(key));
        return (new OpenAiAssistantModel(settings, new HttpClient(transport), logs) { RetryPause = TimeSpan.Zero }, transport, logs);
    }

    [Fact]
    public void Without_a_key_it_is_not_configured()
    {
        Assert.False(Create(HttpStatusCode.OK, "{}", apiKey: " ").Model.IsConfigured);
        Assert.True(Create(HttpStatusCode.OK, "{}").Model.IsConfigured);
    }

    [Fact]
    public async Task It_returns_the_message_text_and_the_tokens_used()
    {
        var (model, _, _) = Create(HttpStatusCode.OK, Completed("zqx part one. ", "zqx part two."));

        var result = await model.NextAsync(Step(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("zqx part one. zqx part two.", result.Value.Text);
        Assert.Empty(result.Value.ToolCalls);
        Assert.Equal(new AssistantModelUsage(120, 30), result.Value.Usage);
    }

    [Fact]
    public async Task The_request_goes_unstored_with_the_data_as_developer_messages_strict_tools_and_sealed_reasoning()
    {
        var (model, transport, _) = Create(HttpStatusCode.OK, Completed("ok"));

        await model.NextAsync(Step(), TestContext.Current.CancellationToken);

        var sent = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.openai.com/v1/responses", sent.Uri);
        Assert.Equal($"Bearer {Key}", sent.Authorization);
        Assert.DoesNotContain(Key, sent.Body, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        Assert.Equal(OpenAiSettings.DefaultChatModel, root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal(["reasoning.encrypted_content"], root.GetProperty("include").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(OpenAiSettings.DefaultReasoningEffort, root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("zqx instructions", root.GetProperty("instructions").GetString());
        Assert.Equal(
            [("developer", "zqx context"), ("user", "zqx first question"), ("assistant", "zqx first reply"), ("user", "zqx second question")],
            root.GetProperty("input").EnumerateArray().Select(item => (item.GetProperty("role").GetString(), item.GetProperty("content").GetString())));
        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal(("function", "search_support_directory", true), (tool.GetProperty("type").GetString(), tool.GetProperty("name").GetString(), tool.GetProperty("strict").GetBoolean()));
        Assert.Equal("object", tool.GetProperty("parameters").GetProperty("type").GetString());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.True(root.GetProperty("parallel_tool_calls").GetBoolean());
    }

    [Fact]
    public async Task Tool_calls_come_back_with_every_output_item_to_hand_back_on_the_next_step()
    {
        var (model, _, _) = Create(HttpStatusCode.OK, ToolCalls);

        var result = await model.NextAsync(Step(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Text);
        Assert.Equal(
            [
                new AssistantToolCall("call_a", "search_support_directory", """{"text":"zqx debt"}"""),
                new AssistantToolCall("call_b", "suggest_app_screen", """{"screen":"services"}"""),
            ],
            result.Value.ToolCalls);
        Assert.Equal(3, result.Value.Produced.Count);
        Assert.Contains("rs_9", result.Value.Produced[0].Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_next_step_sends_back_what_was_produced_verbatim_then_the_tool_outputs_and_may_forbid_tools()
    {
        var (model, transport, _) = Create(HttpStatusCode.OK, Completed("done"));
        var produced = """{"type":"function_call","id":"fc_1","call_id":"call_a","name":"search_support_directory","arguments":"{}"}""";

        await model.NextAsync(
            Step(false, new AssistantModelOutputItem(produced), new AssistantToolOutputItem("call_a", """{"matches":[]}""")),
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(transport.Requests).Body);
        var input = body.RootElement.GetProperty("input").EnumerateArray().ToList();
        Assert.Equal(6, input.Count);
        Assert.Equal(produced, input[4].GetRawText());
        Assert.Equal(
            ("function_call_output", "call_a", """{"matches":[]}"""),
            (input[5].GetProperty("type").GetString(), input[5].GetProperty("call_id").GetString(), input[5].GetProperty("output").GetString()));
        Assert.Equal("none", body.RootElement.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task The_models_and_their_effort_can_be_changed_in_settings()
    {
        var (model, transport, _) = Create(
            HttpStatusCode.OK,
            Completed("ok"),
            more: key => key switch
            {
                OpenAiSettings.ChatModelSetting => " another-model ",
                OpenAiSettings.ChatReasoningEffortSetting => "none",
                _ => null,
            });

        await model.NextAsync(Step(), TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(transport.Requests).Body);
        Assert.Equal("another-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("none", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"zqx provider message"}}""", "status_400")]
    [InlineData(HttpStatusCode.OK, """{"status":"incomplete","output":[]}""", "incomplete")]
    [InlineData(HttpStatusCode.OK, """{"status":"failed","output":[]}""", "not_completed")]
    [InlineData(HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"refusal","refusal":"zqx no"}]}]}""", "refused")]
    [InlineData(HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"   "}]}]}""", "no_text")]
    [InlineData(HttpStatusCode.OK, "zqx not json", "unreadable")]
    public async Task A_failed_call_is_a_model_failure_logged_as_a_fixed_code_only(HttpStatusCode status, string body, string code)
    {
        var (model, transport, logs) = Create(status, body);

        var result = await model.NextAsync(Step(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(AssistantErrors.ModelFailed, result.Error);
        Assert.Single(transport.Requests);
        var line = Assert.Single(logs.Lines);
        Assert.Contains(code, line, StringComparison.Ordinal);
        Assert.DoesNotContain("zqx", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_busy_or_failing_service_is_tried_once_more(HttpStatusCode status)
    {
        var (model, transport, logs) = Create(HttpStatusCode.OK, Completed("ok"));
        transport.Then(status, """{"error":{"message":"zqx busy"}}""");

        var result = await model.NextAsync(Step(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(transport.Requests[0].Body, transport.Requests[1].Body);
        Assert.Contains($"status_{(int)status}_retried", Assert.Single(logs.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_failure_is_final()
    {
        var (model, transport, _) = Create(HttpStatusCode.TooManyRequests, "{}");

        var result = await model.NextAsync(Step(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task A_dropped_connection_is_tried_once_more()
    {
        var (model, transport, _) = Create(HttpStatusCode.OK, Completed("ok"));
        transport.FailNext = 1;

        var result = await model.NextAsync(Step(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task The_memory_is_written_from_the_previous_memory_and_the_conversation_as_data()
    {
        var (model, transport, _) = Create(HttpStatusCode.OK, Completed("- zqx summary"));

        var result = await model.SummarizeAsync(
            new AssistantMemoryRequest("zqx memory instructions", "zqx old", [new AssistantMessage(AssistantMessageRole.User, "zqx said")]),
            TestContext.Current.CancellationToken);

        Assert.Equal("- zqx summary", result.Value);
        using var body = JsonDocument.Parse(Assert.Single(transport.Requests).Body);
        var root = body.RootElement;
        Assert.Equal("zqx memory instructions", root.GetProperty("instructions").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        var input = root.GetProperty("input").EnumerateArray().ToList();
        Assert.Equal(("developer", "Previous memory (data):\nzqx old"), (input[0].GetProperty("role").GetString(), input[0].GetProperty("content").GetString()));
        Assert.Equal("user", input[1].GetProperty("role").GetString());
        Assert.EndsWith("Person: zqx said", input[1].GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_callers_cancellation_stops_the_call()
    {
        var (model, _, _) = Create(HttpStatusCode.OK, Completed("ok"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.NextAsync(Step(), cancelled.Token));
    }
}
