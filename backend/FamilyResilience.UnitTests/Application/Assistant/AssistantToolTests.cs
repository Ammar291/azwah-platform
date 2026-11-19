using System.Diagnostics.Metrics;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.UnitTests.Application.Safeguarding;
using FamilyResilience.UnitTests.Application.SupportResources;
using FamilyResilience.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>The agent's tools and the toolbox that runs them (D-122). Directory entries are synthetic.</summary>
public sealed class AssistantToolTests
{
    private static JsonElement Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>A directory search answering fixed entries, recording the queries it was given.</summary>
    private sealed class ScriptedDirectory(params SupportResource[] matches) : ISupportResourceAssistant
    {
        public List<SupportResourceQuery> Queries { get; } = [];

        public Task<SupportResourceDiscovery> DiscoverAsync(SupportResourceQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(new SupportResourceDiscovery(DiscoveryMode.Deterministic, null, matches));
        }
    }

    private static (SearchSupportDirectoryTool Tool, ScriptedDirectory Directory) Search(params SupportResource[] matches) =>
        Search(new RecordingSafetySignalRecorder(), matches);

    private static (SearchSupportDirectoryTool Tool, ScriptedDirectory Directory) Search(
        RecordingSafetySignalRecorder recorder, params SupportResource[] matches)
    {
        var directory = new ScriptedDirectory(matches);
        var metrics = new SupportResourceMetrics(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());
        var search = new SearchSupportResources(
            directory,
            new FakeCurrentUser(true, "subject", "participant"),
            new CrisisSignalScreen(CrisisSignalCatalog.Load()),
            recorder,
            metrics,
            new FixedTimeProvider(Now),
            new CapturedLogs<SearchSupportResources>());
        return (new SearchSupportDirectoryTool(search), directory);
    }

    [Fact]
    public async Task The_directory_search_never_screens_the_models_words_as_the_persons()
    {
        // The chat screens the person's own message; the model's search words are not the person's and raise nothing.
        var recorder = new RecordingSafetySignalRecorder();
        var (tool, directory) = Search(recorder);

        await tool.InvokeAsync(Arguments("""{"text":"suicide prevention helpline","category":null}"""), AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.Equal("suicide prevention helpline", Assert.Single(directory.Queries).Text);
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task The_directory_search_answers_in_the_persons_language_with_links_marked_information_only()
    {
        var entry = SyntheticSupportResources.Make(code: "synthetic_debt", summaryEn: "Help with debts", summaryAr: "مساعدة في الديون");
        var (tool, directory) = Search(entry);

        var result = await tool.InvokeAsync(Arguments("""{"text":"  debt advice ","category":"financial"}"""), AssistantLanguages.Arabic, TestContext.Current.CancellationToken);

        Assert.Equal("ok", result.Outcome);
        var query = Assert.Single(directory.Queries);
        Assert.Equal("debt advice", query.Text);
        Assert.Equal(["financial"], query.Categories);
        using var output = JsonDocument.Parse(result.Output);
        Assert.Equal("information_only", output.RootElement.GetProperty("notice").GetString());
        var match = Assert.Single(output.RootElement.GetProperty("matches").EnumerateArray());
        Assert.Equal(("مورد تجريبي", "جهة تجريبية", "مساعدة في الديون", "https://example.org/synthetic_debt"), (
            match.GetProperty("name").GetString(), match.GetProperty("provider").GetString(),
            match.GetProperty("summary").GetString(), match.GetProperty("url").GetString()));
        Assert.Equal([new AssistantSourceResponse(entry.Id.Value, "مورد تجريبي", "جهة تجريبية", "https://example.org/synthetic_debt")], result.Sources);
    }

    [Fact]
    public async Task No_match_says_so_and_a_bad_category_is_dropped()
    {
        var (tool, directory) = Search();

        var result = await tool.InvokeAsync(Arguments("""{"text":"anything","category":"Not A Code"}"""), AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.Equal("no_match", result.Outcome);
        Assert.Empty(result.Sources);
        Assert.Empty(Assert.Single(directory.Queries).Categories);
        Assert.Contains("services screen", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Long_text_is_cut_to_200_characters_and_no_text_or_category_is_invalid()
    {
        var (tool, directory) = Search();

        await tool.InvokeAsync(Arguments($$"""{"text":"{{new string('a', 450)}}","category":null}"""), AssistantLanguages.English, TestContext.Current.CancellationToken);
        var empty = await tool.InvokeAsync(Arguments("""{"text":"  ","category":null}"""), AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.Equal(200, Assert.Single(directory.Queries).Text?.Length);
        Assert.Equal("invalid", empty.Outcome);
    }

    [Theory]
    [InlineData("""{"screen":"sos"}""", "ok")]
    [InlineData("""{"screen":"settings"}""", "invalid")]
    [InlineData("""{"screen":3}""", "invalid")]
    [InlineData("""{}""", "invalid")]
    public async Task The_screen_tool_accepts_known_screens_only(string arguments, string outcome)
    {
        var result = await new SuggestAppScreenTool().InvokeAsync(Arguments(arguments), AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(outcome == "ok" ? [AssistantScreens.Sos] : [], result.Actions);
    }

    [Fact]
    public void Every_tool_schema_is_a_strict_json_schema_object()
    {
        foreach (var definition in new IAssistantTool[] { Search().Tool, new SuggestAppScreenTool() }.Select(tool => tool.Definition))
        {
            using var schema = JsonDocument.Parse(definition.ParametersSchema);
            var root = schema.RootElement;
            Assert.Equal("object", root.GetProperty("type").GetString());
            Assert.False(root.GetProperty("additionalProperties").GetBoolean());
            var properties = root.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order();
            Assert.Equal(properties, root.GetProperty("required").EnumerateArray().Select(name => name.GetString()!).Order());
        }

        using var screens = JsonDocument.Parse(new SuggestAppScreenTool().Definition.ParametersSchema);
        Assert.Equal(
            AssistantScreens.All,
            screens.RootElement.GetProperty("properties").GetProperty("screen").GetProperty("enum").EnumerateArray().Select(code => code.GetString()!));
    }

    private sealed class ThrowingTool : IAssistantTool
    {
        public AssistantToolDefinition Definition { get; } = new("throws", "Synthetic.", """{"type":"object"}""");

        public int Running;
        public int MostAtOnce;

        public async Task<AssistantToolResult> InvokeAsync(JsonElement arguments, string language, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref Running);
            MostAtOnce = Math.Max(MostAtOnce, now);
            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref Running);
            if (arguments.TryGetProperty("fail", out _))
            {
                throw new InvalidOperationException("secret words from the person");
            }

            return new AssistantToolResult("{}", "ok", [], []);
        }
    }

    [Fact]
    public async Task The_toolbox_answers_unknown_tools_bad_arguments_and_faults_with_codes_and_runs_one_call_at_a_time()
    {
        var throwing = new ThrowingTool();
        using var toolbox = new AssistantToolbox([throwing, new SuggestAppScreenTool()], TestAssistantMetrics.Create());
        var token = TestContext.Current.CancellationToken;

        var unknown = await toolbox.RunAsync(new AssistantToolCall("1", "nowhere", "{}"), AssistantLanguages.English, token);
        var notJson = await toolbox.RunAsync(new AssistantToolCall("2", "throws", "{nope"), AssistantLanguages.English, token);
        var notObject = await toolbox.RunAsync(new AssistantToolCall("3", "throws", "[1]"), AssistantLanguages.English, token);
        var fault = await toolbox.RunAsync(new AssistantToolCall("4", "throws", """{"fail":true}"""), AssistantLanguages.English, token);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(index =>
            toolbox.RunAsync(new AssistantToolCall($"p{index}", "throws", "{}"), AssistantLanguages.English, token)));

        Assert.Equal(["unknown_tool", "invalid", "invalid", "failed"], new[] { unknown, notJson, notObject, fault }.Select(result => result.Outcome));
        Assert.Equal("""{"error":"failed"}""", fault.Output);
        Assert.Equal(1, throwing.MostAtOnce);
        Assert.Equal(["throws", SuggestAppScreenTool.Name], toolbox.Definitions.Select(definition => definition.Name));
    }
}
