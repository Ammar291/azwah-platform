using System.Diagnostics;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>The agent's loop (D-122) with a scripted model and scripted tools; nothing leaves the test.</summary>
public sealed class AssistantAgentTests
{
    private static readonly AssistantSourceResponse DebtAdvice =
        new(Guid.Parse("0199e0a1-0000-7000-9000-000000002002"), "Household budget and debt advice", "Sample provider B", "https://example.org/debt");

    private static readonly AssistantSourceResponse Parenting =
        new(Guid.Parse("0199e0a1-0000-7000-9000-000000002003"), "Parenting classes", "Sample provider C", "https://example.org/parenting");

    private static AssistantAgentRequest Request(int messages = 1, string? memory = null) =>
        new(null, AssistantLanguages.English, memory, Enumerable.Range(1, messages)
            .Select(index => new AssistantMessage(index % 2 == 1 ? AssistantMessageRole.User : AssistantMessageRole.Assistant, $"Message {index}"))
            .ToList());

    private static AssistantAgent Agent(IAssistantModel model, IAssistantToolRunner? tools = null, IAssistantSafetyCheck? safety = null) =>
        new(model, tools ?? new RecordingToolRunner(), safety ?? new NoAssistantSafetyCheck(), TestAssistantMetrics.Create());

    [Fact]
    public async Task A_tool_call_runs_on_the_server_and_its_output_goes_back_with_what_the_model_produced()
    {
        var model = new ScriptedAssistantModel(
            ScriptedAssistantModel.Calls((SearchSupportDirectoryTool.Name, """{"text":"debt advice","category":null}""")),
            ScriptedAssistantModel.Reply("Household budget and debt advice, from Sample provider B, can help."));
        var tools = new RecordingToolRunner(_ => new AssistantToolResult("""{"matches":[1]}""", "ok", [], [DebtAdvice, Parenting]));

        var result = await Agent(model, tools).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result.Steps);
        var call = Assert.Single(tools.Calls);
        Assert.Equal((SearchSupportDirectoryTool.Name, """{"text":"debt advice","category":null}"""), (call.Name, call.Arguments));
        Assert.Equal(2, model.Steps.Count);
        Assert.True(model.Steps[0].ToolsAllowed);
        Assert.Empty(model.Steps[0].Continuation);
        Assert.Collection(
            model.Steps[1].Continuation,
            produced => Assert.IsType<AssistantModelOutputItem>(produced),
            output => Assert.Equal(new AssistantToolOutputItem(call.CallId, """{"matches":[1]}"""), output));
        Assert.Equal(["Household budget and debt advice"], result.Sources.Select(source => source.Name));
    }

    [Fact]
    public async Task The_last_step_has_no_tools_so_a_model_that_keeps_calling_must_answer()
    {
        var model = new ScriptedAssistantModel(
            ScriptedAssistantModel.Calls((SuggestAppScreenTool.Name, """{"screen":"home"}""")),
            ScriptedAssistantModel.Calls((SuggestAppScreenTool.Name, """{"screen":"help"}""")),
            ScriptedAssistantModel.Calls((SuggestAppScreenTool.Name, """{"screen":"services"}""")),
            ScriptedAssistantModel.Reply("Here you are."));
        var tools = new RecordingToolRunner(call => new AssistantToolResult("{}", "ok", [call.Arguments.Contains("home", StringComparison.Ordinal) ? "home" : "help"], []));

        var result = await Agent(model, tools).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(AssistantAgent.MaxSteps, result.Steps);
        Assert.Equal([true, true, true, false], model.Steps.Select(step => step.ToolsAllowed));
        Assert.Equal(3, tools.Calls.Count);
        Assert.Equal([AssistantScreens.Home, AssistantScreens.Help], result.Actions);
    }

    [Fact]
    public async Task Tool_calls_on_the_last_step_are_not_run_and_without_text_the_turn_fails()
    {
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Calls((SuggestAppScreenTool.Name, """{"screen":"home"}""")));
        var tools = new RecordingToolRunner();

        var result = await Agent(model, tools).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(AssistantAgent.MaxSteps - 1, tools.Calls.Count);
    }

    [Fact]
    public async Task A_failed_step_ends_the_turn()
    {
        var model = new ScriptedAssistantModel(
            ScriptedAssistantModel.Calls((SuggestAppScreenTool.Name, """{"screen":"home"}""")), ScriptedAssistantModel.Fail());

        Assert.Null(await Agent(model).RunAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_reply_is_guarded_into_plain_text()
    {
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("## Steps\n* **Talk** calmly\n* Agree on *one* rule"));

        var result = await Agent(model).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("Steps\n- Talk calmly\n- Agree on one rule", result?.Reply);
    }

    [Fact]
    public async Task Only_sources_the_reply_names_are_returned_once_each()
    {
        var model = new ScriptedAssistantModel(
            ScriptedAssistantModel.Calls((SearchSupportDirectoryTool.Name, """{"text":"a","category":null}"""), (SearchSupportDirectoryTool.Name, """{"text":"b","category":null}""")),
            ScriptedAssistantModel.Reply("Try   PARENTING classes first."));
        var tools = new RecordingToolRunner(_ => new AssistantToolResult("{}", "ok", [], [DebtAdvice, Parenting]));

        var result = await Agent(model, tools).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal([Parenting], result?.Sources);
    }

    [Fact]
    public async Task Danger_found_by_the_safety_check_offers_sos_first_whatever_the_model_said()
    {
        var model = new ScriptedAssistantModel(
            ScriptedAssistantModel.Calls((SuggestAppScreenTool.Name, """{"screen":"services"}""")), ScriptedAssistantModel.Reply("I am here for you."));
        var tools = new RecordingToolRunner(_ => new AssistantToolResult("{}", "ok", [AssistantScreens.Services], []));
        var safety = new FixedSafetyCheck(AssistantSafetyVerdict.DangerOfViolence);

        var result = await Agent(model, tools, safety).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal([AssistantScreens.Sos, AssistantScreens.Services], result?.Actions);
        Assert.Equal(["Message 1"], safety.Checked);
    }

    [Theory]
    [InlineData(AssistantSafetyVerdict.Clear, false)]
    [InlineData(AssistantSafetyVerdict.DangerToSelf, true)]
    [InlineData(AssistantSafetyVerdict.DangerOfViolence, true)]
    public async Task Agent_result_carries_the_safety_verdict(AssistantSafetyVerdict verdict, bool sos)
    {
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("I am here for you."));

        var result = await Agent(model, safety: new FixedSafetyCheck(verdict)).RunAsync(Request(), TestContext.Current.CancellationToken);
        var withoutCheck = await Agent(new ScriptedAssistantModel(ScriptedAssistantModel.Reply("Fine."))).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(verdict, result?.Safety);
        Assert.Equal(sos, result!.Actions.Contains(AssistantScreens.Sos));
        Assert.Equal(AssistantSafetyVerdict.NotChecked, withoutCheck?.Safety);
    }

    [Theory]
    [InlineData(AssistantSafetyVerdict.NotChecked, false)]
    [InlineData(AssistantSafetyVerdict.Clear, false)]
    [InlineData(AssistantSafetyVerdict.DangerToSelf, true)]
    [InlineData(AssistantSafetyVerdict.DangerOfViolence, true)]
    public void Only_the_two_danger_verdicts_are_danger(AssistantSafetyVerdict verdict, bool danger) =>
        Assert.Equal(danger, verdict.IsDanger());

    [Theory]
    [InlineData(AssistantSafetyVerdict.DangerToSelf, SafetyConcern.SuicideOrSelfHarm)]
    [InlineData(AssistantSafetyVerdict.DangerOfViolence, SafetyConcern.AbuseOrDanger)]
    public void A_danger_verdict_is_recorded_as_its_concern(AssistantSafetyVerdict verdict, SafetyConcern concern) =>
        Assert.Equal(concern, verdict.Concern());

    [Theory]
    [InlineData(AssistantSafetyVerdict.NotChecked)]
    [InlineData(AssistantSafetyVerdict.Clear)]
    public void A_verdict_without_danger_has_no_concern(AssistantSafetyVerdict verdict) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => verdict.Concern());

    [Fact]
    public async Task A_safety_check_slower_than_the_grace_changes_nothing_and_does_not_hold_the_reply()
    {
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("Fine."));
        var safety = new FixedSafetyCheck(AssistantSafetyVerdict.DangerToSelf, delay: TimeSpan.FromSeconds(30));
        var clock = Stopwatch.StartNew();

        var result = await Agent(model, safety: safety).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Empty(result!.Actions);
        Assert.Equal(AssistantSafetyVerdict.NotChecked, result.Safety);
        Assert.True(clock.Elapsed < AssistantAgent.SideTaskGrace + TimeSpan.FromSeconds(5), $"Took {clock.Elapsed}.");
    }

    [Fact]
    public async Task A_short_conversation_keeps_its_memory_unchanged()
    {
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("Fine."));

        var result = await Agent(model).RunAsync(Request(AssistantMemory.RefreshAt - 1, memory: "old"), TestContext.Current.CancellationToken);

        Assert.Null(result?.Memory);
        Assert.Empty(model.MemoryRequests);
    }

    [Fact]
    public async Task A_long_conversation_refreshes_the_memory_from_the_old_one_and_all_but_the_latest_messages()
    {
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("Fine."))
        {
            Summary = (_, _) => Task.FromResult(Result.Success("  - **Folded** memory  ")),
        };
        var request = Request(31, memory: "  old memory ");

        var result = await Agent(model).RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("- Folded memory", result?.Memory);
        var refresh = Assert.Single(model.MemoryRequests);
        Assert.Equal("old memory", refresh.PreviousMemory);
        Assert.Equal(request.Messages.Take(31 - AssistantMemory.KeepRecent), refresh.Messages);
        Assert.Equal(AssistantPrompt.MemoryInstructions(AssistantLanguages.English), refresh.Instructions);
    }

    [Fact]
    public async Task A_failed_memory_refresh_keeps_the_old_memory_and_the_reply()
    {
        var model = new ScriptedAssistantModel(ScriptedAssistantModel.Reply("Fine."))
        {
            Summary = (_, _) => Task.FromException<Result<string>>(new InvalidOperationException("Synthetic fault.")),
        };

        var result = await Agent(model).RunAsync(Request(35), TestContext.Current.CancellationToken);

        Assert.Equal(("Fine.", (string?)null), (result?.Reply, result?.Memory));
    }

    [Fact]
    public void Screens_are_known_codes_sos_first_distinct_and_at_most_three()
    {
        Assert.Equal(
            [AssistantScreens.Sos, AssistantScreens.Home, AssistantScreens.Help],
            AssistantScreens.Order(["home", "nowhere", "help", "home", "sos", "services", "resilience"]));
        Assert.Equal(AssistantScreens.MaxPerReply, AssistantScreens.Order(AssistantScreens.All).Count);
    }
}
