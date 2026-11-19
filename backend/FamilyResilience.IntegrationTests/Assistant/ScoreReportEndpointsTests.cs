using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>
/// The grading scale on a result and the written report over HTTP, on <see cref="ParticipantApi"/>: section A's lowest
/// answers score 2 on a 2 to 6 scale, <c>band_low</c>; section B has no scoring rules. The model is a fake answering from
/// the test, so nothing leaves it. Every family and every question is synthetic.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class ScoreReportEndpointsTests(ParticipantApi api)
{
    private static readonly object English = new { language = "en" };

    private ApiFactory Factory => api.Factory;

    private static string Report(Guid sessionId) => $"/api/v1/sessions/{sessionId}/report";

    private Task<Enrolment> HeadAsync(CancellationToken cancellationToken) =>
        Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));

    private async Task<Guid> ScoredSessionAsync(Enrolment enrolment, Guid memberId, CancellationToken cancellationToken)
    {
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, memberId, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return sessionId;
    }

    private Task<List<AuditEvent>> AuditsAsync(string subject, CancellationToken cancellationToken) =>
        Factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == subject && audit.Action == GenerateScoreReport.AuditAction)
            .ToListAsync(cancellationToken));

    [Fact]
    public async Task The_result_gives_the_head_the_grading_scale_from_the_approved_rule_set()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);

        using var response = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);

        var scale = (await response.ReadAsync<SessionResultResponse>(cancellationToken)).Scale;
        Assert.NotNull(scale);
        Assert.Equal((2, 6), (scale.Minimum, scale.Maximum));
        Assert.Equal(
            [("band_low", 2, 3), ("band_high", 4, 6)],
            scale.Bands.Select(band => (band.Code, band.Min, band.Max)));
    }

    [Fact]
    public async Task Anyone_but_the_head_gets_no_scale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(self: false), Rosters.Spouse(self: true));
        var head = enrolment.Members.Single(member => !member.IsCaller).Id;
        var sessionId = await ScoredSessionAsync(enrolment, head, cancellationToken);

        using var response = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);

        Assert.Null((await response.ReadAsync<SessionResultResponse>(cancellationToken)).Scale);
    }

    [Fact]
    public async Task An_unscored_session_has_no_scale_while_it_waits_for_approved_rules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Aged(68), "B", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);

        using var response = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);

        var result = await response.ReadAsync<SessionResultResponse>(cancellationToken);
        Assert.Equal((ResultState.Pending, (object?)null), (result.State, result.Scale));
    }

    [Fact]
    public async Task Without_a_model_a_report_is_503_unavailable_and_nothing_is_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);

        using var response = await enrolment.Client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("assistant.unavailable", await response.ProblemCodeAsync(cancellationToken));
        Assert.Empty(await AuditsAsync(enrolment.Subject, cancellationToken));
    }

    [Fact]
    public async Task The_head_gets_a_report_built_on_the_recorded_score_and_it_is_audited_without_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.ReadAsync<ScoreReportResponse>(cancellationToken);
        // The total, band and scale are the platform's record: the model's text cannot carry or change them.
        Assert.Equal((sessionId, "en", 2m, "band_low", 2, 6), (report.SessionId, report.Language, report.TotalScore, report.TierCode, report.Scale.Minimum, report.Scale.Maximum));
        Assert.Equal(RecordingScoreReportModel.Usable.Summary, report.Summary);
        Assert.Equal(RecordingScoreReportModel.Usable.Strengths, report.Strengths);
        Assert.Equal(RecordingScoreReportModel.Usable.Weaknesses, report.Weaknesses);
        var step = Assert.Single(report.NextSteps);
        var expected = RecordingScoreReportModel.Usable.Improvements[0];
        Assert.Equal((expected.Area, expected.Reason), (step.Area, step.Reason));
        Assert.Equal(expected.Steps, step.Steps);
        Assert.Equal(RecordingScoreReportModel.Name, report.ModelName);
        Assert.Equal(TestData.Now, report.GeneratedAt);

        var sent = Assert.Single(model.Requests);
        Assert.Equal(("en", 2m, "band_low", 2, 6), (sent.Language, sent.TotalScore, sent.BandCode, sent.Minimum, sent.Maximum));
        Assert.NotEmpty(sent.Answers);
        Assert.All(sent.Answers, answer => Assert.Equal((1m, 3), (answer.Points, answer.MaximumPoints)));

        var audit = Assert.Single(await AuditsAsync(enrolment.Subject, cancellationToken));
        Assert.Equal(("assessment_session", sessionId.ToString(), AuditOutcome.Succeeded), (audit.ResourceType, audit.ResourceId, audit.Outcome));
    }

    [Fact]
    public async Task What_goes_to_the_model_names_no_person_household_or_record()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var input = ScoreReportPrompt.Input(Assert.Single(model.Requests));
        foreach (var identifier in new[] { sessionId.ToString(), enrolment.Self.ToString(), enrolment.Subject, enrolment.Household.Id.ToString(), enrolment.Household.Reference })
        {
            Assert.DoesNotContain(identifier, input, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_report_comes_in_the_language_asked_for()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), new { language = "ar" }, cancellationToken);

        Assert.Equal("ar", (await response.ReadAsync<ScoreReportResponse>(cancellationToken)).Language);
        Assert.Equal("ar", Assert.Single(model.Requests).Language);
    }

    [Fact]
    public async Task A_manager_answering_for_the_head_is_refused_and_nothing_goes_to_the_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(self: false), Rosters.Spouse(self: true));
        var head = enrolment.Members.Single(member => !member.IsCaller).Id;
        var sessionId = await ScoredSessionAsync(enrolment, head, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("assistant.score_report_head_only", await response.ProblemCodeAsync(cancellationToken));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task An_unscored_session_is_409_and_nothing_goes_to_the_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Aged(68), "B", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("assistant.score_report_not_ready", await response.ProblemCodeAsync(cancellationToken));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Another_households_session_is_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(owner, owner.Self, cancellationToken);
        using var stranger = await HeadAsync(cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(stranger.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(model.Requests);
    }

    [Theory]
    [InlineData("""{"language":"fr"}""")]
    [InlineData("""{}""")]
    public async Task A_language_other_than_en_or_ar_is_400(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostRawJsonAsync(Report(sessionId), body, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Anonymous_callers_get_401_and_staff_get_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = Factory.As(null);
        using var researcher = Factory.AsResearcher();

        using var unauthenticated = await anonymous.PostJsonAsync(Report(Guid.NewGuid()), English, cancellationToken);
        using var forbidden = await researcher.PostJsonAsync(Report(Guid.NewGuid()), English, cancellationToken);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden), (unauthenticated.StatusCode, forbidden.StatusCode));
    }

    public static TheoryData<string> ModelFailures => new() { "failure", "throws", "empty", "too_long" };

    [Theory]
    [MemberData(nameof(ModelFailures))]
    public async Task A_model_that_fails_or_writes_nothing_usable_is_502_and_audited_as_failed(string failure)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel(failure);
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("assistant.model_failed", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(AuditOutcome.Failed, Assert.Single(await AuditsAsync(enrolment.Subject, cancellationToken)).Outcome);
    }

    [Fact]
    public async Task A_person_who_leaves_while_the_model_writes_still_leaves_an_audit_of_the_data_sent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        var model = new RecordingScoreReportModel("hangs");
        await using var host = Factory.With(report: model);
        using var client = host.Client(enrolment.Subject);
        using var leaving = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var request = client.PostJsonAsync(Report(sessionId), English, leaving.Token);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await leaving.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        var audits = await AuditsAsync(enrolment.Subject, cancellationToken);
        for (var attempt = 0; audits.Count == 0 && attempt < 50; attempt++)
        {
            await Task.Delay(100, cancellationToken);
            audits = await AuditsAsync(enrolment.Subject, cancellationToken);
        }

        Assert.Equal(AuditOutcome.Failed, Assert.Single(audits).Outcome);
    }

    [Fact]
    public async Task Neither_the_answers_nor_the_report_reach_a_log()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        var logs = new CapturingLoggerProvider();
        var model = new RecordingScoreReportModel();
        await using var host = Factory.With(report: model, logs: logs);
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var question = Assert.Single(model.Requests).Answers[0].Question;
        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry =>
            entry.Message.Contains(question, StringComparison.Ordinal)
            || entry.Message.Contains(RecordingScoreReportModel.Usable.Summary, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_wordy_report_keeps_two_strengths_two_weaknesses_and_three_steps_each_within_its_budget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel("wordy") { WholeReportCodes = ["A", "A", "A", "A"] });
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.ReadAsync<ScoreReportResponse>(cancellationToken);
        var sentence = RecordingScoreReportModel.WordySentence;
        Assert.Equal(string.Join(' ', Enumerable.Repeat(sentence, 3)), report.Summary);
        Assert.Equal((2, 2, 3), (report.Strengths.Count, report.Weaknesses.Count, report.NextSteps.Count));
        Assert.All(report.Strengths.Concat(report.Weaknesses), point => Assert.Equal(sentence, point.Detail));
        Assert.All(report.NextSteps, step =>
        {
            Assert.Equal(sentence, step.Reason);
            Assert.Equal([sentence, sentence], step.Steps);
        });
    }

    [Fact]
    public async Task The_response_holds_only_the_named_fields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await HeadAsync(cancellationToken);
        var sessionId = await ScoredSessionAsync(enrolment, enrolment.Self, cancellationToken);
        await using var host = Factory.With(report: new RecordingScoreReportModel());
        using var client = host.Client(enrolment.Subject);

        using var response = await client.PostJsonAsync(Report(sessionId), English, cancellationToken);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            ["sessionId", "language", "totalScore", "tierCode", "scale", "summary", "strengths", "weaknesses", "nextSteps", "modelName", "generatedAt"],
            json.RootElement.EnumerateObject().Select(property => property.Name));
    }
}
