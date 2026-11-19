using System.Net;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Domain.Audit;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>
/// Writing a report ahead, as the person answers (D-113), on <see cref="ParticipantApi"/>: section A has approved scoring,
/// section B has none. The model is a fake answering from the test, so nothing leaves it. Every family is synthetic.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class ScoreReportPreparationTests(ParticipantApi api)
{
    private static readonly object English = new { language = "en" };

    private ApiFactory Factory => api.Factory;

    private static string Eligibility(Guid sessionId) => $"/api/v1/sessions/{sessionId}/report/eligibility";

    private static string Part(Guid sessionId, string section = "A") => $"/api/v1/sessions/{sessionId}/report/parts/{section}";

    private static string Summary(Guid sessionId) => $"/api/v1/sessions/{sessionId}/report/summary";

    private Task<Enrolment> HeadAsync(CancellationToken cancellationToken) =>
        Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));

    private Task<Guid> AnsweredAsync(Enrolment enrolment, Guid memberId, CancellationToken cancellationToken, string section = "A") =>
        Journeys.AnsweredSessionAsync(enrolment, api, memberId, section, 0, cancellationToken);

    private Task<List<AuditEvent>> AuditsAsync(string subject, string action, CancellationToken cancellationToken) =>
        Factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == subject && audit.Action == action)
            .ToListAsync(cancellationToken));

    private static async Task<bool> EligibleAsync(HttpClient client, Guid sessionId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(Eligibility(sessionId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<ScoreReportEligibilityResponse>(cancellationToken)).Eligible;
    }

    [Fact]
    public async Task A_head_answering_a_session_with_approved_scoring_is_eligible_before_submitting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel());
        using var client = host.Client(enrolment.Subject);

        Assert.True(await EligibleAsync(client, sessionId, cancellationToken));
    }

    [Fact]
    public async Task Without_a_model_nobody_is_eligible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);

        Assert.False(await EligibleAsync(enrolment.Client, sessionId, cancellationToken));
    }

    [Fact]
    public async Task A_manager_answering_for_the_head_is_not_eligible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(self: false), Rosters.Spouse(self: true));
        var head = enrolment.Members.Single(member => !member.IsCaller).Id;
        var sessionId = await AnsweredAsync(enrolment, head, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel());
        using var client = host.Client(enrolment.Subject);

        Assert.False(await EligibleAsync(client, sessionId, cancellationToken));
    }

    [Fact]
    public async Task A_session_without_approved_scoring_is_not_eligible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Aged(68), cancellationToken, "B");
        await using var host = Factory.With(report: new RecordingScoreReportModel());
        using var client = host.Client(enrolment.Subject);

        Assert.False(await EligibleAsync(client, sessionId, cancellationToken));
    }

    [Fact]
    public async Task Another_households_session_is_404_for_every_endpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(owner, owner.Self, cancellationToken);
        using var stranger = await HeadAsync(cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(stranger.Subject);

        using var eligibility = await client.GetAsync(Eligibility(sessionId), cancellationToken);
        using var part = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);
        using var summary = await client.PostJsonAsync(Summary(sessionId), English, cancellationToken);

        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound),
            (eligibility.StatusCode, part.StatusCode, summary.StatusCode));
        Assert.Empty(model.PartRequests);
    }

    [Fact]
    public async Task A_part_is_written_before_submission_from_the_servers_own_answers_and_audited_without_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var part = await response.ReadAsync<ScoreReportPartResponse>(cancellationToken);
        Assert.Equal(("A", "en", RecordingScoreReportModel.Name), (part.SectionCode, part.Language, part.ModelName));
        Assert.Equal(RecordingScoreReportModel.UsablePart.Strengths, part.Strengths);
        Assert.Equal(RecordingScoreReportModel.UsablePart.Weaknesses, part.Weaknesses);
        var sent = Assert.Single(model.PartRequests);
        Assert.Equal("en", sent.Language);
        Assert.NotEmpty(sent.Answers);
        Assert.All(sent.Answers, answer => Assert.Equal((1m, 3), (answer.Points, answer.MaximumPoints)));
        var audit = Assert.Single(await AuditsAsync(enrolment.Subject, WriteScoreReportPart.AuditAction, cancellationToken));
        Assert.Equal(("assessment_session", sessionId.ToString(), AuditOutcome.Succeeded), (audit.ResourceType, audit.ResourceId, audit.Outcome));
    }

    private const string Arabic = "نص تجريبي";

    /// <summary>
    /// A synthetic graph: every question of section A leads to one need, addressed by one current service, one past its
    /// review date and one by a clinic, while a second need nobody's answers raise has a service of its own.
    /// </summary>
    private SupportKnowledgeGraph SyntheticGraph()
    {
        static string Text(string english) => $$"""{"en":"{{english}}","ar":"{{Arabic}}"}""";
        static string Entry(string code, string need, string due, string kind = "government") => $$"""
            {"code":"{{code}}","kind":"{{kind}}","name":{{Text($"Name of {code}")}},"purpose":{{Text("A purpose")}},"emirates":["abu_dhabi"],
             "website":"https://example.org/{{code}}","phone":"+971 2 000 0000",
             "services":[{"code":"{{code}}_service","name":{{Text($"Service of {code}")}},"summary":{{Text("A summary")}},"needs":["{{need}}"],
               "procedure":[{{Text("Book a visit")}},{{Text("Bring your papers")}}],"documents":[{{Text("An identity card")}}],
               "website":"https://example.org/{{code}}/service","via":null,"evidence":"opened","sources":["https://example.org/source"]}],
             "sources":["https://example.org/source"],"approvalReference":"SYNTHETIC","checkedOn":"2020-01-01","reviewDue":"{{due}}"}
            """;
        var signals = string.Join(',', api.Section("A").Items.Select(item => "\"" + item.Code + "\":[\"need_a\"]"));
        return SupportKnowledgeGraph.Parse($$"""
            {"schema":"support-knowledge-graph.v1",
             "needs":[{"code":"need_a","label":{{Text("Need A")}}},{"code":"need_b","label":{{Text("Need B")}}}],
             "signals":{{{signals}}},
             "authorities":[{{Entry("in_area", "need_a", "2999-01-01")}},{{Entry("overdue", "need_a", "2020-06-01")}},{{Entry("clinic", "need_a", "2999-01-01", "private")}},{{Entry("elsewhere", "need_b", "2999-01-01")}}]}
            """);
    }

    [Fact]
    public async Task A_parts_next_step_carries_what_the_graph_reached_word_for_word_and_a_model_picks_none_for_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel(), graph: SyntheticGraph());
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        var step = Assert.Single((await response.ReadAsync<ScoreReportPartResponse>(cancellationToken)).NextSteps);
        Assert.Equal(("A", 0m), (step.SectionCode, step.Standing));
        // No pick, so the graph's order among public services decides: not the clinic, the overdue entry or the other need.
        var authority = Assert.Single(step.Authorities);
        Assert.Equal(("in_area", "government", "Name of in_area", "https://example.org/in_area"), (authority.Code, authority.Kind, authority.Name, authority.Website));
        Assert.Equal(["abu_dhabi"], authority.Emirates);
        var service = Assert.Single(authority.Services);
        Assert.Equal(("Service of in_area", "Need A", "https://example.org/in_area/service"), (service.Name, service.Need, service.Website));
        Assert.Equal(["Book a visit", "Bring your papers"], service.Procedure);
        Assert.Equal(["An identity card"], service.Documents);
    }

    [Fact]
    public async Task A_models_pick_chooses_among_what_the_graph_reached_and_cannot_add_to_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel { PartSupportCodes = ["invented_ministry", "elsewhere_service", "overdue_service", "clinic_service"] };
        await using var host = Factory.With(report: model, graph: SyntheticGraph());
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        var authority = Assert.Single(Assert.Single((await response.ReadAsync<ScoreReportPartResponse>(cancellationToken)).NextSteps).Authorities);
        Assert.Equal(("clinic", "private"), (authority.Code, authority.Kind));
    }

    [Fact]
    public async Task An_authority_comes_in_the_language_asked_for()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel(), graph: SyntheticGraph());
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), new { language = "ar" }, cancellationToken);

        var authority = Assert.Single(Assert.Single((await response.ReadAsync<ScoreReportPartResponse>(cancellationToken)).NextSteps).Authorities);
        Assert.Equal((Arabic, Arabic), (authority.Name, Assert.Single(authority.Services).Procedure[0]));
    }

    [Fact]
    public async Task When_the_graph_reaches_nothing_a_step_is_still_written_with_no_authority()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel());
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        Assert.Empty(Assert.Single((await response.ReadAsync<ScoreReportPartResponse>(cancellationToken)).NextSteps).Authorities);
    }

    [Fact]
    public async Task The_model_is_offered_only_what_the_graph_reached_and_never_a_procedure_or_contact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model, graph: SyntheticGraph());
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        var input = ScoreReportPrompt.Input(Assert.Single(model.PartRequests));
        Assert.Contains("in_area_service", input, StringComparison.Ordinal);
        Assert.DoesNotContain("elsewhere", input, StringComparison.Ordinal);
        Assert.DoesNotContain("overdue", input, StringComparison.Ordinal);
        Assert.DoesNotContain("Book a visit", input, StringComparison.Ordinal);
        Assert.DoesNotContain("+971", input, StringComparison.Ordinal);
        Assert.DoesNotContain("example.org", input, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_whole_report_links_each_step_by_the_models_section_code_and_never_guesses_an_authority()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        var model = new RecordingScoreReportModel { WholeReportCodes = ["made_up_code", "A"] };
        await using var host = Factory.With(report: model, graph: SyntheticGraph());
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync($"/api/v1/sessions/{sessionId}/report", English, cancellationToken);

        var steps = (await response.ReadAsync<ScoreReportResponse>(cancellationToken)).NextSteps;
        Assert.Equal(2, steps.Count);
        // The real area first, because it stands lowest; the unknown code last, with no authority guessed for it.
        Assert.Equal("in_area", Assert.Single(steps[0].Authorities).Code);
        Assert.Equal((string.Empty, 0), (steps[1].SectionCode, steps[1].Authorities.Count));
        Assert.Contains("\"sectionCode\":\"A\"", ScoreReportPrompt.Input(Assert.Single(model.Requests)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_goes_to_the_model_holds_no_score_band_person_household_or_record()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var part = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);
        using var summary = await client.PostJsonAsync(Summary(sessionId), English, cancellationToken);

        var inputs = ScoreReportPrompt.Input(Assert.Single(model.PartRequests)) + ScoreReportPrompt.Input(Assert.Single(model.SummaryRequests));
        foreach (var identifier in new[] { sessionId.ToString(), enrolment.Self.ToString(), enrolment.Subject, enrolment.Household.Id.ToString(), enrolment.Household.Reference, "band_low", "totalScore" })
        {
            Assert.DoesNotContain(identifier, inputs, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_part_can_still_be_written_after_submission_to_fill_a_gap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel());
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_part_needs_its_section_answered_and_a_summary_needs_every_section()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await Journeys.StartedSessionAsync(enrolment, api, enrolment.Self, "A", cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var part = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);
        using var summary = await client.PostJsonAsync(Summary(sessionId), English, cancellationToken);

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (part.StatusCode, summary.StatusCode));
        Assert.Equal("assistant.score_report_not_ready", await part.ProblemCodeAsync(cancellationToken));
        Assert.Empty(model.PartRequests);
        Assert.Empty(model.SummaryRequests);
    }

    [Fact]
    public async Task A_section_outside_the_session_is_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId, "B"), English, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(model.PartRequests);
    }

    [Fact]
    public async Task A_summary_covers_the_session_and_is_audited_without_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Summary(sessionId), new { language = "ar" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.ReadAsync<ScoreReportSummaryResponse>(cancellationToken);
        Assert.Equal(("ar", RecordingScoreReportModel.UsableSummary.Summary), (summary.Language, summary.Summary));
        var sent = Assert.Single(model.SummaryRequests);
        Assert.Equal("ar", sent.Language);
        Assert.NotEmpty(sent.Areas);
        Assert.NotEmpty(sent.Answers);
        Assert.Single(await AuditsAsync(enrolment.Subject, WriteScoreReportSummary.AuditAction, cancellationToken));
    }

    [Fact]
    public async Task A_part_with_too_many_entries_keeps_the_first_ones_rather_than_wasting_the_call()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel("too_many"));
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var part = await response.ReadAsync<ScoreReportPartResponse>(cancellationToken);
        Assert.Equal(["First"], part.Weaknesses.Select(point => point.Title));
    }

    [Fact]
    public async Task A_wordy_part_is_cut_to_its_budgets_and_says_where_its_area_stands()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel("wordy"));
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var part = await response.ReadAsync<ScoreReportPartResponse>(cancellationToken);
        // Every section A answer took its lowest option, so the area stands at the bottom of its range.
        Assert.Equal(0m, part.Standing);
        var weakness = Assert.Single(part.Weaknesses);
        Assert.Equal(RecordingScoreReportModel.WordySentence, weakness.Detail);
        var step = Assert.Single(part.NextSteps);
        Assert.Equal(ScoreReportWordBudget.MaxSteps, step.Steps.Count);
        Assert.True(ScoreReportWordBudget.Count(step.Reason) <= ScoreReportWordBudget.ReasonWords);
    }

    [Fact]
    public async Task A_wordy_summary_ends_at_the_last_sentence_within_its_budget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel("wordy"));
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Summary(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.ReadAsync<ScoreReportSummaryResponse>(cancellationToken);
        Assert.Equal(
            string.Join(' ', Enumerable.Repeat(RecordingScoreReportModel.WordySentence, ScoreReportWordBudget.SummaryWords / ScoreReportWordBudget.Count(RecordingScoreReportModel.WordySentence))),
            summary.Summary);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("throws")]
    [InlineData("empty")]
    public async Task A_model_that_fails_is_502_and_audited_as_failed(string failure)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel(failure));
        using var client = host.Client(enrolment.Subject);

        using var part = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);
        using var summary = await client.PostJsonAsync(Summary(sessionId), English, cancellationToken);

        Assert.Equal((HttpStatusCode.BadGateway, HttpStatusCode.BadGateway), (part.StatusCode, summary.StatusCode));
        Assert.Equal(AuditOutcome.Failed, Assert.Single(await AuditsAsync(enrolment.Subject, WriteScoreReportPart.AuditAction, cancellationToken)).Outcome);
    }

    [Fact]
    public async Task A_manager_answering_for_the_head_is_refused_and_nothing_goes_to_the_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(self: false), Rosters.Spouse(self: true));
        var head = enrolment.Members.Single(member => !member.IsCaller).Id;
        var sessionId = await AnsweredAsync(enrolment, head, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(model.PartRequests);
    }

    [Fact]
    public async Task Without_a_model_a_part_is_503()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);

        using var response = await enrolment.Client.PostJsonAsync(Part(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"language":"fr"}""")]
    [InlineData("""{}""")]
    public async Task A_language_other_than_en_or_ar_is_400(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel());
        using var client = host.Client(enrolment.Subject);

        using var part = await client.PostRawJsonAsync(Part(sessionId), body, cancellationToken);
        using var summary = await client.PostRawJsonAsync(Summary(sessionId), body, cancellationToken);

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (part.StatusCode, summary.StatusCode));
    }

    [Fact]
    public async Task Anonymous_callers_get_401_and_staff_get_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = Factory.As(null);
        using var researcher = Factory.AsResearcher();
        var id = Guid.NewGuid();

        using var unauthenticated = await anonymous.GetAsync(Eligibility(id), cancellationToken);
        using var unauthenticatedPart = await anonymous.PostJsonAsync(Part(id), English, cancellationToken);
        using var forbidden = await researcher.GetAsync(Eligibility(id), cancellationToken);
        using var forbiddenSummary = await researcher.PostJsonAsync(Summary(id), English, cancellationToken);

        Assert.Equal(
            (HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            (unauthenticated.StatusCode, unauthenticatedPart.StatusCode, forbidden.StatusCode, forbiddenSummary.StatusCode));
    }

    [Fact]
    public async Task Neither_the_answers_nor_the_writing_reach_a_log()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await AnsweredAsync(enrolment, enrolment.Self, cancellationToken);
        var logs = new CapturingLoggerProvider();
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model, logs: logs);
        using var client = host.Client(enrolment.Subject);

        using var part = await client.PostJsonAsync(Part(sessionId), English, cancellationToken);
        using var summary = await client.PostJsonAsync(Summary(sessionId), English, cancellationToken);

        var question = Assert.Single(model.PartRequests).Answers[0].Question;
        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry =>
            entry.Message.Contains(question, StringComparison.Ordinal)
            || entry.Message.Contains(RecordingScoreReportModel.UsableSummary.Summary, StringComparison.Ordinal)
            || entry.Message.Contains(RecordingScoreReportModel.UsablePart.Strengths[0].Title, StringComparison.Ordinal));
    }
}
