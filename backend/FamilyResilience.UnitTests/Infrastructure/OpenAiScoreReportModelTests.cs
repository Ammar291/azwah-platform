using System.Net;
using System.Text;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Infrastructure.Assistant;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>The OpenAI adapter against a fake transport: no request leaves the test, and the key and answers are synthetic.</summary>
public sealed class OpenAiScoreReportModelTests
{
    private const string Key = "sk-synthetic-test-key";

    private static readonly ScoreReportFacts Facts = new(
        "en",
        96m,
        39,
        156,
        "transitional",
        [new ScoreBandResponse("vulnerable", 39, 78), new ScoreBandResponse("transitional", 79, 117), new ScoreBandResponse("flourishing", 118, 156)],
        [new ScoreReportArea("Synthetic area", 10m, 4, 16)],
        [new ScoreReportAnswer("Synthetic area", "zqxsyntheticquestionmarker", "Often", 3m, 4)]);

    private const string Writing =
        """{"summary":"S","strengths":[{"title":"T","detail":"D"}],"weaknesses":[{"title":"W","detail":"D"}],"improvements":[{"area":"A","reason":"R","steps":["One"],"supportCodes":["a_service"]}]}""";

    [Fact]
    public async Task A_models_picks_are_read_back_for_the_server_to_check()
    {
        var (model, _, _) = Create(HttpStatusCode.OK, Completed(Writing));

        var result = await model.WriteAsync(Facts, TestContext.Current.CancellationToken);

        Assert.Equal(["a_service"], Assert.Single(result.Value.Improvements).SupportCodes);
    }

    private static string Completed(string text) => JsonSerializer.Serialize(new
    {
        status = "completed",
        output = new object[]
        {
            new { type = "reasoning", summary = Array.Empty<object>() },
            new { type = "message", content = new object[] { new { type = "output_text", text } } },
        },
    });

    private static (OpenAiScoreReportModel Model, FakeTransport Transport, CapturedLogs Logs) Create(
        HttpStatusCode status, string body, string? apiKey = Key, string? modelName = null)
    {
        var transport = new FakeTransport(status, body);
        var logs = new CapturedLogs();
        return (new OpenAiScoreReportModel(Settings(apiKey, modelName), new HttpClient(transport), logs), transport, logs);
    }

    private static OpenAiSettings Settings(string? apiKey = Key, string? modelName = null, string? chatModel = null) =>
        OpenAiSettings.From(key => key switch
        {
            OpenAiSettings.ApiKeySetting => apiKey,
            OpenAiSettings.ReportModelSetting => modelName,
            OpenAiSettings.ChatModelSetting => chatModel,
            _ => null,
        });

    [Fact]
    public void Without_a_key_it_is_not_configured_and_it_names_the_default_model()
    {
        var (model, _, _) = Create(HttpStatusCode.OK, "{}", apiKey: "  ");

        Assert.False(model.IsConfigured);
        Assert.Equal(OpenAiSettings.DefaultReportModel, model.ModelName);
    }

    [Fact]
    public void The_model_name_can_be_changed_in_settings()
    {
        var (model, _, _) = Create(HttpStatusCode.OK, "{}", modelName: "another-model");

        Assert.True(model.IsConfigured);
        Assert.Equal("another-model", model.ModelName);
    }

    [Fact]
    public async Task The_report_keeps_its_own_model_when_the_assistants_chat_model_changes()
    {
        var transport = new FakeTransport(HttpStatusCode.OK, Completed(Writing));
        var model = new OpenAiScoreReportModel(Settings(chatModel: "another-chat-model"), new HttpClient(transport), new CapturedLogs());

        await model.WriteAsync(Facts, TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(transport.Body);
        Assert.Equal(OpenAiSettings.DefaultReportModel, json.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task It_reads_the_writing_from_the_message_and_ignores_other_output()
    {
        var (model, _, _) = Create(HttpStatusCode.OK, Completed(Writing));

        var result = await model.WriteAsync(Facts, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("S", result.Value.Summary);
        Assert.Equal("T", Assert.Single(result.Value.Strengths).Title);
        Assert.Equal("W", Assert.Single(result.Value.Weaknesses).Title);
        var improvement = Assert.Single(result.Value.Improvements);
        Assert.Equal(("A", "R", "One"), (improvement.Area, improvement.Reason, Assert.Single(improvement.Steps)));
    }

    [Fact]
    public async Task The_request_goes_to_the_responses_endpoint_unstored_with_a_strict_schema_and_the_key_only_in_its_header()
    {
        var (model, transport, _) = Create(HttpStatusCode.OK, Completed(Writing));

        await model.WriteAsync(Facts, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, transport.Method);
        Assert.Equal("https://api.openai.com/v1/responses", transport.Uri?.ToString());
        Assert.Equal($"Bearer {Key}", transport.Authorization);
        Assert.DoesNotContain(Key, transport.Body, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(transport.Body);
        var root = json.RootElement;
        Assert.Equal(OpenAiSettings.DefaultReportModel, root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Contains("zqxsyntheticquestionmarker", root.GetProperty("input").GetString(), StringComparison.Ordinal);
        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        AssertStrict(format.GetProperty("schema"));
    }

    /// <summary>Strict mode needs every object closed and every property required, all the way down.</summary>
    private static void AssertStrict(JsonElement schema)
    {
        if (schema.GetProperty("type").GetString() == "array")
        {
            AssertStrict(schema.GetProperty("items"));
            return;
        }

        if (schema.GetProperty("type").GetString() != "object")
        {
            return;
        }

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var properties = schema.GetProperty("properties").EnumerateObject().ToList();
        Assert.Equal(
            properties.Select(property => property.Name).Order(StringComparer.Ordinal),
            schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()!).Order(StringComparer.Ordinal));
        properties.ForEach(property => AssertStrict(property.Value));
    }

    [Fact]
    public async Task A_part_asks_for_no_reasoning_with_its_own_strict_schema_and_reads_its_text()
    {
        const string Part = """{"strengths":[{"title":"T","detail":"D"}],"weaknesses":[],"improvements":[{"area":"A","reason":"R","steps":["One"]}]}""";
        var (model, transport, _) = Create(HttpStatusCode.OK, Completed(Part));

        var result = await model.WritePartAsync(new ScoreReportPartFacts("en", "Synthetic area", Facts.Answers), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("T", Assert.Single(result.Value.Strengths).Title);
        Assert.Empty(result.Value.Weaknesses);
        using var json = JsonDocument.Parse(transport.Body);
        Assert.False(json.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("none", json.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        var format = json.RootElement.GetProperty("text").GetProperty("format");
        Assert.True(format.GetProperty("strict").GetBoolean());
        AssertStrict(format.GetProperty("schema"));
    }

    [Fact]
    public async Task A_summary_asks_for_no_reasoning_with_its_own_strict_schema_and_reads_its_text()
    {
        var (model, transport, _) = Create(HttpStatusCode.OK, Completed("""{"summary":"A summary."}"""));

        var result = await model.WriteSummaryAsync(new ScoreReportSummaryFacts("ar", Facts.Areas, Facts.Answers), TestContext.Current.CancellationToken);

        Assert.Equal("A summary.", result.Value.Summary);
        using var json = JsonDocument.Parse(transport.Body);
        Assert.Equal("none", json.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Contains("Modern Standard Arabic", json.RootElement.GetProperty("instructions").GetString(), StringComparison.Ordinal);
        AssertStrict(json.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema"));
    }

    [Fact]
    public async Task A_part_or_summary_failure_logs_a_fixed_code_only()
    {
        var (model, _, logs) = Create(HttpStatusCode.TooManyRequests, """{"error":{"message":"zqxsecretmarker"}}""");

        var part = await model.WritePartAsync(new ScoreReportPartFacts("en", "Synthetic area", Facts.Answers), TestContext.Current.CancellationToken);
        var summary = await model.WriteSummaryAsync(new ScoreReportSummaryFacts("en", Facts.Areas, Facts.Answers), TestContext.Current.CancellationToken);

        Assert.Equal((AssistantErrors.ModelFailed, AssistantErrors.ModelFailed), (part.Error, summary.Error));
        Assert.All(logs.Messages, message => Assert.DoesNotContain("zqxsecretmarker", message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_dropped_connection_gets_one_more_try_with_the_same_request()
    {
        var transport = new DroppingTransport(drops: 1, Completed("""{"summary":"A summary."}"""));
        var model = new OpenAiScoreReportModel(Settings(), new HttpClient(transport), new CapturedLogs());

        var result = await model.WriteSummaryAsync(new ScoreReportSummaryFacts("en", Facts.Areas, Facts.Answers), TestContext.Current.CancellationToken);

        Assert.Equal("A summary.", result.Value.Summary);
        Assert.Equal(2, transport.Bodies.Count);
        Assert.Equal(transport.Bodies[0], transport.Bodies[1]);
    }

    [Fact]
    public async Task A_second_dropped_connection_is_a_failure_logged_as_fixed_codes_only()
    {
        var transport = new DroppingTransport(drops: 2, Completed("""{"summary":"A summary."}"""));
        var logs = new CapturedLogs();
        var model = new OpenAiScoreReportModel(Settings(), new HttpClient(transport), logs);

        var result = await model.WriteSummaryAsync(new ScoreReportSummaryFacts("en", Facts.Areas, Facts.Answers), TestContext.Current.CancellationToken);

        Assert.Equal(AssistantErrors.ModelFailed, result.Error);
        Assert.Equal(2, transport.Bodies.Count);
        Assert.Collection(
            logs.Messages,
            message => Assert.Contains("connection_retried", message, StringComparison.Ordinal),
            message => Assert.EndsWith("connection.", message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_answer_from_openai_is_never_retried()
    {
        var (model, transport, _) = Create(HttpStatusCode.TooManyRequests, "{}");

        await model.WriteSummaryAsync(new ScoreReportSummaryFacts("en", Facts.Areas, Facts.Answers), TestContext.Current.CancellationToken);

        Assert.Equal(1, transport.Calls);
    }

    public static TheoryData<HttpStatusCode, string, string> Failures => new()
    {
        { HttpStatusCode.Unauthorized, """{"error":{"message":"zqxsecretmarker"}}""", "status_401" },
        { HttpStatusCode.TooManyRequests, """{"error":{"message":"zqxsecretmarker"}}""", "status_429" },
        { HttpStatusCode.OK, """{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[]}""", "not_completed" },
        { HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"refusal","refusal":"zqxsecretmarker"}]}]}""", "refused" },
        { HttpStatusCode.OK, """{"status":"completed","output":[]}""", "no_text" },
        { HttpStatusCode.OK, """not json zqxsecretmarker""", "unreadable" },
        { HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"not json zqxsecretmarker"}]}]}""", "unreadable" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_bad_answer_is_a_failure_logged_as_a_fixed_code_only(HttpStatusCode status, string body, string code)
    {
        var (model, _, logs) = Create(status, body);

        var result = await model.WriteAsync(Facts, TestContext.Current.CancellationToken);

        Assert.Equal(AssistantErrors.ModelFailed, result.Error);
        var message = Assert.Single(logs.Messages);
        Assert.Contains(code, message, StringComparison.Ordinal);
        Assert.DoesNotContain("zqxsecretmarker", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, message, StringComparison.Ordinal);
    }

    private sealed class FakeTransport(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? Uri { get; private set; }

        public string? Authorization { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class DroppingTransport(int drops, string body) : HttpMessageHandler
    {
        private int _dropsLeft = drops;

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (_dropsLeft-- > 0)
            {
                throw new HttpRequestException("Synthetic dropped connection.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class CapturedLogs : ILogger<OpenAiScoreReportModel>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
