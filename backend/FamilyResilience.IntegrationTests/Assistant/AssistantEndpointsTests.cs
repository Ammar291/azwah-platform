using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Infrastructure.Assistant;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>
/// The text assistant over HTTP. The shared host has no model, as production does; tests that need one run a host of
/// their own with a fake model that answers from the test. Every family is synthetic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AssistantEndpointsTests(ApiFactory factory)
{
    private const string Replies = "/api/v1/assistant/replies";
    private const string Availability = "/api/v1/assistant/availability";
    private const string CrisisMessage = "I want to end my life";

    private static object Chat(string text = "How is my family doing?", string language = "en") => new
    {
        language,
        messages = new object[]
        {
            new { role = "user", text = "Hello" },
            new { role = "assistant", text = "Hello, how can I help?" },
            new { role = "user", text },
        },
    };

    private Task<List<AuditEvent>> AuditsAsync(string subject, string action, CancellationToken cancellationToken) =>
        factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == subject && audit.Action == action)
            .ToListAsync(cancellationToken));

    [Fact]
    public async Task Availability_says_neither_is_switched_on_without_a_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsParticipant();

        using var response = await client.GetAsync(Availability, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"assistant":false,"consultant":false,"scoreReport":false}""", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Availability_says_each_is_switched_on_when_its_model_is_configured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(
            assistant: RecordingAssistantModel.Replying("unused"), consultant: new EchoVoiceModel(), report: new RecordingScoreReportModel());
        using var client = host.Client(ApiClients.NewSubject("participant"));

        using var response = await client.GetAsync(Availability, cancellationToken);

        Assert.Equal("""{"assistant":true,"consultant":true,"scoreReport":true}""", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task With_an_openai_key_the_openai_models_are_registered_and_switched_on()
    {
        // Availability only: nothing here calls OpenAI, and the key is synthetic.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(OpenAiSettings.ApiKeySetting, "sk-synthetic-test-key"));
        using var client = host.Client(ApiClients.NewSubject("participant"));

        using var response = await client.GetAsync(Availability, cancellationToken);

        // One key switches on all three; the report shares the assistant's settings record.
        Assert.Equal("""{"assistant":true,"consultant":true,"scoreReport":true}""", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.IsType<OpenAiAssistantModel>(host.Services.GetRequiredService<IAssistantModel>());
        Assert.IsType<OpenAiLiveConsultantModel>(host.Services.GetRequiredService<IVoiceConsultantModel>());
        Assert.IsType<OpenAiModerationCheck>(host.Services.GetRequiredService<IAssistantSafetyCheck>());
        Assert.IsType<OpenAiScoreReportModel>(host.Services.GetRequiredService<IScoreReportModel>());
    }

    [Fact]
    public async Task Without_a_model_a_reply_is_503_unavailable_and_nothing_is_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        using var client = factory.AsParticipant(subject);

        using var response = await client.PostJsonAsync(Replies, Chat(), cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("assistant.unavailable", await response.ProblemCodeAsync(cancellationToken));
        Assert.Empty(await AuditsAsync(subject, GenerateAssistantReply.AuditAction, cancellationToken));
    }

    [Fact]
    public async Task A_reply_comes_from_the_model_with_the_callers_context_and_is_audited_without_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(factory, cancellationToken, Rosters.Head(), Rosters.Child(9));
        var model = RecordingAssistantModel.Replying("A synthetic reply.");
        await using var host = factory.With(assistant: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Replies, Chat(), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            ["reply", "generatedAt", "actions", "sources", "memory", "safetyNotice"],
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("A synthetic reply.", json.RootElement.GetProperty("reply").GetString());
        Assert.Equal(TestData.Now, json.RootElement.GetProperty("generatedAt").GetDateTimeOffset());
        Assert.Equal(0, json.RootElement.GetProperty("actions").GetArrayLength());
        Assert.Equal(0, json.RootElement.GetProperty("sources").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("memory").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("safetyNotice").ValueKind);
        var sent = Assert.Single(model.Steps);
        Assert.Equal(AssistantPrompt.ChatInstructions(AssistantLanguages.English), sent.Instructions);
        Assert.Equal(4, sent.Input.Count);
        Assert.Equal(AssistantInputKind.User, sent.Input[^1].Kind);
        Assert.Equal([SearchSupportDirectoryTool.Name, SuggestAppScreenTool.Name], sent.Tools.Select(tool => tool.Name));
        var context = RecordingAssistantModel.Context(sent);
        Assert.Equal(enrolment.Household.Reference, context.GetProperty("household").GetProperty("reference").GetString());
        Assert.Equal([42, 9], context.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("age").GetInt32()).OrderDescending());
        Assert.True(context.GetProperty("caller").GetProperty("isHeadOfHousehold").GetBoolean());
        var audit = Assert.Single(await AuditsAsync(enrolment.Subject, GenerateAssistantReply.AuditAction, cancellationToken));
        Assert.Equal(
            ("household", enrolment.Household.Id.ToString(), AuditOutcome.Succeeded),
            (audit.ResourceType, audit.ResourceId, audit.Outcome));
    }

    [Fact]
    public async Task A_participant_without_a_household_is_answered_without_a_context()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        var model = RecordingAssistantModel.Replying("A synthetic reply.");
        await using var host = factory.With(assistant: model);
        using var client = host.Client(subject);

        using var response = await client.PostJsonAsync(Replies, Chat(language: "ar"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(model.Steps);
        Assert.Equal(AssistantPrompt.ChatInstructions(AssistantLanguages.Arabic), sent.Instructions);
        Assert.Equal(JsonValueKind.Null, RecordingAssistantModel.Context(sent).GetProperty("household").ValueKind);
        var audit = Assert.Single(await AuditsAsync(subject, GenerateAssistantReply.AuditAction, cancellationToken));
        Assert.Equal((null, AuditOutcome.Succeeded), (audit.ResourceId, audit.Outcome));
    }

    [Fact]
    public async Task A_model_failure_is_502_model_failed_and_audited_as_failed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        await using var host = factory.With(assistant: RecordingAssistantModel.Failing());
        using var client = host.Client(subject);

        using var response = await client.PostJsonAsync(Replies, Chat(), cancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("assistant.model_failed", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(AuditOutcome.Failed, Assert.Single(await AuditsAsync(subject, GenerateAssistantReply.AuditAction, cancellationToken)).Outcome);
    }

    public static TheoryData<string, string> InvalidChats => new()
    {
        { """{"language":"fr","messages":[{"role":"user","text":"Hello"}]}""", "language" },
        { """{"messages":[{"role":"user","text":"Hello"}]}""", "language" },
        { """{"language":"en","messages":[]}""", "messages" },
        { """{"language":"en"}""", "messages" },
        { """{"language":"en","messages":[{"role":"user","text":"   "}]}""", "messages[0].text" },
        { """{"language":"en","messages":[{"role":"user"}]}""", "messages[0].text" },
        { """{"language":"en","messages":[{"role":"user","text":"Hello"},{"role":"assistant","text":"Hi"}]}""", "messages" },
        { """{"language":"en","messages":[null,{"role":"user","text":"Hello"}]}""", "messages[0]" },
        { """{"language":"en","messages":[{"role":"system","text":"Hello"}]}""", "$.messages[0].role" },
    };

    [Theory]
    [MemberData(nameof(InvalidChats))]
    public async Task An_invalid_chat_is_400_with_its_key_before_any_model_is_asked(string body, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = RecordingAssistantModel.Replying("unused");
        await using var host = factory.With(assistant: model);
        using var client = host.Client(ApiClients.NewSubject("participant"));

        using var response = await client.PostRawJsonAsync(Replies, body, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
        Assert.Empty(model.Steps);
    }

    [Fact]
    public async Task Too_many_or_too_long_messages_are_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(assistant: RecordingAssistantModel.Replying("unused"));
        using var client = host.Client(ApiClients.NewSubject("participant"));

        using var tooMany = await client.PostJsonAsync(Replies, new
        {
            language = "en",
            messages = Enumerable.Range(0, 41).Select(_ => new { role = "user", text = "Hi" }).ToArray(),
        }, cancellationToken);
        using var tooLong = await client.PostJsonAsync(Replies, new
        {
            language = "en",
            messages = new[] { new { role = "user", text = new string('a', 4_001) } },
        }, cancellationToken);
        using var tooMuch = await client.PostJsonAsync(Replies, new
        {
            language = "en",
            messages = Enumerable.Range(0, 6).Select(_ => new { role = "user", text = new string('a', 4_000) }).ToArray(),
        }, cancellationToken);

        Assert.Contains("messages", await tooMany.ErrorKeysAsync(cancellationToken));
        Assert.Contains("messages[0].text", await tooLong.ErrorKeysAsync(cancellationToken));
        Assert.Contains("messages", await tooMuch.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData("GET", Availability)]
    [InlineData("POST", Replies)]
    public async Task Anonymous_callers_get_401_and_staff_get_403(string method, string url)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = factory.As(null);
        using var researcher = factory.AsResearcher();
        using var coordinator = factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);

        using var unauthenticated = await Send(anonymous, method, url, cancellationToken);
        using var forbidden = await Send(researcher, method, url, cancellationToken);
        using var alsoForbidden = await Send(coordinator, method, url, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, alsoForbidden.StatusCode);
    }

    [Fact]
    public async Task Neither_the_chat_nor_the_reply_reaches_a_log()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string Question = "zqxsyntheticquestionmarker";
        const string Reply = "zqxsyntheticreplymarker";
        var logs = new CapturingLoggerProvider();
        await using var host = factory.With(assistant: RecordingAssistantModel.Replying(Reply), logs: logs);
        using var client = host.Client(ApiClients.NewSubject("participant"));
        await using var failing = factory.With(assistant: RecordingAssistantModel.Failing(), logs: logs);
        using var failingClient = failing.Client(ApiClients.NewSubject("participant"));

        using var answered = await client.PostJsonAsync(Replies, Chat(Question), cancellationToken);
        using var failed = await failingClient.PostJsonAsync(Replies, Chat(Question), cancellationToken);
        using var invalid = await client.PostRawJsonAsync(
            Replies, $$"""{"language":"en","messages":[{"role":"user","text":"{{Question}}"},{"role":"assistant","text":"x"}]}""", cancellationToken);

        Assert.Equal(
            (HttpStatusCode.OK, HttpStatusCode.BadGateway, HttpStatusCode.BadRequest),
            (answered.StatusCode, failed.StatusCode, invalid.StatusCode));
        Assert.DoesNotContain(await invalid.Content.ReadAsStringAsync(cancellationToken), Question, StringComparison.Ordinal);
        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry =>
            entry.Message.Contains(Question, StringComparison.Ordinal) || entry.Message.Contains(Reply, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Roles_are_read_as_the_app_writes_them()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = RecordingAssistantModel.Replying("A synthetic reply.");
        await using var host = factory.With(assistant: model);
        using var client = host.Client(ApiClients.NewSubject("participant"));

        using var response = await client.PostRawJsonAsync(
            Replies, """{"language":"en","messages":[{"role":"assistant","text":"Hi"},{"role":"user","text":"Hello"}]}""", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [AssistantMessageRole.Assistant, AssistantMessageRole.User],
            Assert.Single(model.Steps).Input.Skip(1).Select(input => input.Kind == AssistantInputKind.User ? AssistantMessageRole.User : AssistantMessageRole.Assistant));
    }

    [Fact]
    public async Task The_memory_and_lines_said_on_a_call_reach_the_model_and_a_long_chat_gets_a_fresh_memory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = RecordingAssistantModel.Replying("A synthetic reply.");
        await using var host = factory.With(assistant: model);
        using var client = host.Client(ApiClients.NewSubject("participant"));
        var messages = Enumerable.Range(0, AssistantMemory.RefreshAt - 1)
            .Select(index => (object)new { role = index % 2 == 0 ? "user" : "assistant", text = $"Synthetic line {index}", channel = index == 1 ? "voice" : "chat" })
            .Append(new { role = "user", text = "Synthetic last question", channel = "chat" })
            .ToList();

        using var response = await client.PostJsonAsync(Replies, new { language = "en", memory = " Synthetic memory ", messages }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("- Synthetic refreshed memory", json.RootElement.GetProperty("memory").GetString());
        var sent = Assert.Single(model.Steps);
        Assert.Equal($"{AssistantPrompt.MemoryHeading}\nSynthetic memory", sent.Input[1].Text);
        Assert.Equal(AssistantPrompt.VoiceMarker + "Synthetic line 1", sent.Input[3].Text);
        var refresh = Assert.Single(model.MemoryRequests);
        Assert.Equal(AssistantMemory.RefreshAt - AssistantMemory.KeepRecent, refresh.Messages.Count);
    }

    [Fact]
    public async Task Screens_the_agent_offers_come_back_as_actions_and_bad_tool_calls_are_answered_not_thrown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = RecordingAssistantModel.Calling(
            "Here is the place to look.",
            (SuggestAppScreenTool.Name, """{"screen":"services"}"""),
            (SuggestAppScreenTool.Name, """{"screen":"nowhere"}"""),
            ("not_a_tool", "{}"),
            (SearchSupportDirectoryTool.Name, """{"text":"zqx nothing approved matches this","category":null}"""));
        await using var host = factory.With(assistant: model);
        using var client = host.Client(ApiClients.NewSubject("participant"));

        using var response = await client.PostJsonAsync(Replies, Chat(), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(["services"], json.RootElement.GetProperty("actions").EnumerateArray().Select(action => action.GetString()));
        Assert.Equal(0, json.RootElement.GetProperty("sources").GetArrayLength());
        var outputs = model.Steps[1].Continuation.OfType<AssistantToolOutputItem>().Select(output => output.Output).ToList();
        Assert.Equal(4, outputs.Count);
        Assert.Equal("""{"error":"invalid"}""", outputs[1]);
        Assert.Equal("""{"error":"unknown_tool"}""", outputs[2]);
        Assert.Contains("\"matches\":[]", outputs[3], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"language":"en","memory":"MEMORY","messages":[{"role":"user","text":"Hi"}]}""", "memory")]
    [InlineData("""{"language":"en","messages":[{"role":"user","text":"Hi","channel":"radio"}]}""", "$.messages[0].channel")]
    public async Task A_memory_too_long_or_an_unknown_channel_is_400(string body, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var model = RecordingAssistantModel.Replying("unused");
        await using var host = factory.With(assistant: model);
        using var client = host.Client(ApiClients.NewSubject("participant"));

        using var response = await client.PostRawJsonAsync(
            Replies, body.Replace("MEMORY", new string('m', AssistantMemory.MaxLength + 1), StringComparison.Ordinal), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
        Assert.Empty(model.Steps);
    }

    [Fact]
    public async Task Crisis_message_raises_a_critical_alert_and_returns_crisis_notice_with_sos_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(factory, cancellationToken, Rosters.Head(), Rosters.Child(9));
        var model = RecordingAssistantModel.Calling("I am here for you.", (SuggestAppScreenTool.Name, """{"screen":"services"}"""));
        await using var host = factory.With(assistant: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Replies, Chat(CrisisMessage), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(SafetyNotices.CrisisSupport, json.RootElement.GetProperty("safetyNotice").GetString());
        Assert.Equal(
            [AssistantScreens.Sos, AssistantScreens.Services],
            json.RootElement.GetProperty("actions").EnumerateArray().Select(action => action.GetString()));
        var alert = Assert.Single(await factory.AlertsOfAsync(enrolment.Subject, cancellationToken));
        Assert.Equal(
            (SafetyConcern.SuicideOrSelfHarm, SafetySeverity.Critical, SafetyAlertSource.AssistantChat, SafetyDetector.PhraseScreen),
            (alert.Concern, alert.Severity, alert.Source, alert.Detector));
        Assert.Equal(("suicide.intent", "en", 1, SafetyAlertStatus.Open), (alert.SignalCode, alert.Language, alert.OccurrenceCount, alert.Status));
        Assert.Equal((enrolment.Household.Id, enrolment.Self), (alert.HouseholdId?.Value, alert.MemberId?.Value));
        // The detector acted, so the System actor is audited: no trail under the person's subject outlives erasure.
        var raised = Assert.Single(await factory.AlertAuditsAsync(alert.Id, cancellationToken));
        Assert.Equal(
            (RecordSafetySignal.Raised, AuditActorType.System, (string?)null, RecordSafetySignal.ResourceType),
            (raised.Action, raised.ActorType, raised.ActorId, raised.ResourceType));
        Assert.Empty(await AuditsAsync(enrolment.Subject, RecordSafetySignal.Raised, cancellationToken));
        var stored = await factory.StoredRowsOfAsync(enrolment.Subject, cancellationToken);
        Assert.Contains(stored, row => row.Contains(alert.Id.Value.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(stored, row => row.Contains("end my life", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Repeated_crisis_message_increments_the_same_alert()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        await using var host = factory.With(assistant: RecordingAssistantModel.Replying("I am here for you."));
        using var client = host.Client(subject);

        using var first = await client.PostJsonAsync(Replies, Chat(CrisisMessage), cancellationToken);
        using var second = await client.PostJsonAsync(Replies, Chat("I still want to kill myself"), cancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal((2, "suicide.intent", SafetyAlertStatus.Open), (alert.OccurrenceCount, alert.SignalCode, alert.Status));
        var audits = await factory.AlertAuditsAsync(alert.Id, cancellationToken);
        Assert.Equal([RecordSafetySignal.Raised, RecordSafetySignal.Repeated], audits.Select(audit => audit.Action).Order(StringComparer.Ordinal));
        Assert.All(audits, audit => Assert.Equal((AuditActorType.System, (string?)null), (audit.ActorType, audit.ActorId)));
    }

    [Fact]
    public async Task Arabic_crisis_message_raises_an_alert()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        await using var host = factory.With(assistant: RecordingAssistantModel.Replying("أنا هنا معك."));
        using var client = host.Client(subject);

        using var response = await client.PostJsonAsync(Replies, Chat("ابي اموت", language: "ar"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(SafetyNotices.CrisisSupport, json.RootElement.GetProperty("safetyNotice").GetString());
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal(
            (SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, "suicide.wish_to_die", "ar"),
            (alert.Concern, alert.Detector, alert.SignalCode, alert.Language));
    }

    [Fact]
    public async Task Ordinary_message_raises_nothing_and_has_no_notice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        await using var host = factory.With(assistant: RecordingAssistantModel.Replying("A synthetic reply."), safety: new FixedAssistantSafetyCheck(AssistantSafetyVerdict.Clear));
        using var client = host.Client(subject);

        using var response = await client.PostJsonAsync(Replies, Chat("This traffic is killing me, how do we plan the week?"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("safetyNotice").ValueKind);
        Assert.Equal(0, json.RootElement.GetProperty("actions").GetArrayLength());
        Assert.Empty(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.DoesNotContain(await factory.StoredRowsOfAsync(subject, cancellationToken), row => row.Contains("safeguarding.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Crisis_message_with_a_failing_model_still_records_and_problem_carries_safetyNotice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        await using var host = factory.With(assistant: RecordingAssistantModel.Failing());
        using var client = host.Client(subject);

        using var response = await client.PostJsonAsync(Replies, Chat(CrisisMessage), cancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("assistant.model_failed", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(SafetyNotices.CrisisSupport, problem.RootElement.GetProperty("safetyNotice").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal(502, problem.RootElement.GetProperty("status").GetInt32());
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal((SafetyAlertSource.AssistantChat, SafetyDetector.PhraseScreen, "suicide.intent"), (alert.Source, alert.Detector, alert.SignalCode));
        Assert.Equal(AuditOutcome.Failed, Assert.Single(await AuditsAsync(subject, GenerateAssistantReply.AuditAction, cancellationToken)).Outcome);
    }

    [Fact]
    public async Task Crisis_message_without_a_model_is_503_with_safetyNotice_and_still_records()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        using var client = factory.AsParticipant(subject);

        using var response = await client.PostJsonAsync(Replies, Chat(CrisisMessage), cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            ("assistant.unavailable", SafetyNotices.CrisisSupport),
            (problem.RootElement.GetProperty("code").GetString(), problem.RootElement.GetProperty("safetyNotice").GetString()));
        Assert.Equal(SafetyDetector.PhraseScreen, Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken)).Detector);
        Assert.Empty(await AuditsAsync(subject, GenerateAssistantReply.AuditAction, cancellationToken));
    }

    [Fact]
    public async Task A_failure_without_a_crisis_signal_has_no_safetyNotice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.With(assistant: RecordingAssistantModel.Failing());
        using var client = host.Client(ApiClients.NewSubject("participant"));

        using var response = await client.PostJsonAsync(Replies, Chat(), cancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(problem.RootElement.TryGetProperty("safetyNotice", out _));
    }

    [Fact]
    public async Task Moderation_danger_without_phrase_match_raises_ai_moderation_alert()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        var safety = new FixedAssistantSafetyCheck(AssistantSafetyVerdict.DangerToSelf);
        await using var host = factory.With(assistant: RecordingAssistantModel.Replying("I am here for you."), safety: safety);
        using var client = host.Client(subject);

        using var response = await client.PostJsonAsync(Replies, Chat("zqx words the phrase list does not hold"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(SafetyNotices.CrisisSupport, json.RootElement.GetProperty("safetyNotice").GetString());
        Assert.Equal(AssistantScreens.Sos, json.RootElement.GetProperty("actions")[0].GetString());
        Assert.Equal(1, safety.Checked);
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal(
            (SafetyConcern.SuicideOrSelfHarm, SafetyAlertSource.AssistantChat, SafetyDetector.AiModeration, (string?)null, "en"),
            (alert.Concern, alert.Source, alert.Detector, alert.SignalCode, alert.Language));
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string url, CancellationToken cancellationToken) =>
        method == "GET" ? client.GetAsync(url, cancellationToken) : client.PostJsonAsync(url, Chat(), cancellationToken);
}
