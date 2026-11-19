using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Infrastructure.Assistant;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>The safety check against a fake transport (D-126); texts are synthetic.</summary>
public sealed class OpenAiModerationCheckTests
{
    private static string Answer(string flagged, double score) =>
        "{\"results\":[{\"flagged\":true,\"categories\":{\"" + flagged + "\":true},\"category_scores\":{\"" + flagged + "\":"
        + score.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}]}";

    private static (OpenAiModerationCheck Check, FakeOpenAiTransport Transport, CapturedLogs<OpenAiModerationCheck> Logs) Create(HttpStatusCode status, string body)
    {
        var transport = new FakeOpenAiTransport(status, body);
        var logs = new CapturedLogs<OpenAiModerationCheck>();
        var settings = OpenAiSettings.From(key => key == OpenAiSettings.ApiKeySetting ? "sk-synthetic" : null);
        return (new OpenAiModerationCheck(settings, new HttpClient(transport), logs), transport, logs);
    }

    [Theory]
    [InlineData("self-harm", 0.2, AssistantSafetyVerdict.DangerToSelf)]
    [InlineData("self-harm/intent", 0.9, AssistantSafetyVerdict.DangerToSelf)]
    [InlineData("violence", 0.3, AssistantSafetyVerdict.Clear)]
    [InlineData("violence", 0.8, AssistantSafetyVerdict.DangerOfViolence)]
    [InlineData("harassment/threatening", 0.7, AssistantSafetyVerdict.DangerOfViolence)]
    [InlineData("sexual", 0.99, AssistantSafetyVerdict.Clear)]
    public async Task Self_harm_or_serious_violence_is_danger(string category, double score, AssistantSafetyVerdict expected)
    {
        var (check, transport, _) = Create(HttpStatusCode.OK, Answer(category, score));

        var verdict = await check.CheckAsync("zqx synthetic words", TestContext.Current.CancellationToken);

        Assert.Equal(expected, verdict);
        var sent = Assert.Single(transport.Requests);
        Assert.Equal("https://api.openai.com/v1/moderations", sent.Uri);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal((OpenAiSettings.DefaultModerationModel, "zqx synthetic words"), (body.RootElement.GetProperty("model").GetString(), body.RootElement.GetProperty("input").GetString()));
    }

    [Fact]
    public async Task Verdict_flags_self_harm_as_danger_to_self()
    {
        // Self-harm wins over violence, in the same result and across results.
        const string SameResult =
            """{"results":[{"flagged":true,"categories":{"violence":true,"self-harm/intent":true},"category_scores":{"violence":0.95,"self-harm/intent":0.3}}]}""";
        const string LaterResult =
            """{"results":[{"flagged":true,"categories":{"violence":true},"category_scores":{"violence":0.95}},{"flagged":true,"categories":{"self-harm":true},"category_scores":{"self-harm":0.1}}]}""";

        var same = await Create(HttpStatusCode.OK, SameResult).Check.CheckAsync("zqx synthetic words", TestContext.Current.CancellationToken);
        var later = await Create(HttpStatusCode.OK, LaterResult).Check.CheckAsync("zqx synthetic words", TestContext.Current.CancellationToken);

        Assert.Equal((AssistantSafetyVerdict.DangerToSelf, AssistantSafetyVerdict.DangerToSelf), (same, later));
        Assert.True(same.IsDanger());
    }

    [Theory]
    [InlineData("violence", 0.6, AssistantSafetyVerdict.DangerOfViolence)]
    [InlineData("violence/graphic", 0.75, AssistantSafetyVerdict.DangerOfViolence)]
    [InlineData("hate/threatening", 0.6, AssistantSafetyVerdict.DangerOfViolence)]
    [InlineData("harassment/threatening", 0.59, AssistantSafetyVerdict.Clear)]
    public async Task Verdict_flags_violence_over_threshold_as_danger_to_others(string category, double score, AssistantSafetyVerdict expected)
    {
        var (check, _, _) = Create(HttpStatusCode.OK, Answer(category, score));

        var verdict = await check.CheckAsync("zqx synthetic words", TestContext.Current.CancellationToken);

        Assert.Equal(expected, verdict);
        Assert.Equal(expected != AssistantSafetyVerdict.Clear, verdict.IsDanger());
    }

    [Fact]
    public async Task Several_texts_go_in_one_request_and_get_one_verdict_each_in_order()
    {
        const string Results =
            """{"results":[{"flagged":false,"categories":{},"category_scores":{}},{"flagged":true,"categories":{"self-harm/intent":true},"category_scores":{"self-harm/intent":0.97}},{"flagged":true,"categories":{"violence":true},"category_scores":{"violence":0.9}}]}""";
        var (check, transport, _) = Create(HttpStatusCode.OK, Results);

        var verdicts = await check.CheckManyAsync(["zqx one", "  ", "zqx two", "zqx three"], TestContext.Current.CancellationToken);

        Assert.Equal(
            [AssistantSafetyVerdict.Clear, AssistantSafetyVerdict.Clear, AssistantSafetyVerdict.DangerToSelf, AssistantSafetyVerdict.DangerOfViolence],
            verdicts);
        using var body = JsonDocument.Parse(Assert.Single(transport.Requests).Body);
        Assert.Equal(["zqx one", "zqx two", "zqx three"], body.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task A_batch_that_fails_or_comes_back_short_is_not_checked_for_any_text()
    {
        var (failing, _, logs) = Create(HttpStatusCode.InternalServerError, """{"error":"zqx provider text"}""");
        var cut = Create(HttpStatusCode.OK, """{"results":[{"flagged":false,"categories":{},"category_scores":{}}]}""").Check;

        var failed = await failing.CheckManyAsync(["zqx one", "zqx two"], TestContext.Current.CancellationToken);
        var shortened = await cut.CheckManyAsync(["zqx one", "zqx two"], TestContext.Current.CancellationToken);

        Assert.Equal([AssistantSafetyVerdict.NotChecked, AssistantSafetyVerdict.NotChecked], failed);
        Assert.Equal([AssistantSafetyVerdict.NotChecked, AssistantSafetyVerdict.NotChecked], shortened);
        Assert.DoesNotContain("zqx", Assert.Single(logs.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_flagged_is_clear_and_failures_are_not_checked_with_a_fixed_code_only()
    {
        var clear = await Create(HttpStatusCode.OK, """{"results":[{"flagged":false,"categories":{},"category_scores":{}}]}""").Check
            .CheckAsync("zqx", TestContext.Current.CancellationToken);
        var (failing, _, logs) = Create(HttpStatusCode.InternalServerError, """{"error":"zqx provider text"}""");
        var failed = await failing.CheckAsync("zqx", TestContext.Current.CancellationToken);
        var unreadable = await Create(HttpStatusCode.OK, "zqx not json").Check.CheckAsync("zqx", TestContext.Current.CancellationToken);

        Assert.Equal(AssistantSafetyVerdict.Clear, clear);
        Assert.Equal(AssistantSafetyVerdict.NotChecked, failed);
        Assert.Equal(AssistantSafetyVerdict.NotChecked, unreadable);
        Assert.DoesNotContain("zqx", Assert.Single(logs.Lines), StringComparison.Ordinal);
    }
}
