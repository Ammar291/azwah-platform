using System.Net;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// One request answers a session for a demonstration's band and submits it (D-143), on a host with
/// <c>DemoTools:Enabled</c> derived from the participant host, so it shares its approved section A rules (totals 2–6:
/// band_low 2–3, band_high 4–6). The answers are saved, then submitted, scored and routed by the same use cases a
/// person's own answers go through.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class DemoSubmissionEndpointTests(ParticipantApi api) : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _demo =
        api.Factory.WithWebHostBuilder(builder => builder.UseSetting(DemoToolsSettings.EnabledKey, "true"));

    public async ValueTask DisposeAsync() => await _demo.DisposeAsync();

    private HttpClient Demo(Enrolment enrolment) => _demo.As(enrolment.Subject, Roles.Participant);

    private static async Task<HttpResponseMessage> SubmitDemoAsync(
        HttpClient client, Guid sessionId, string? level, Guid? key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/demo-submission")
        {
            Content = JsonContent(level),
        };
        if (key is { } idempotencyKey)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static StringContent JsonContent(string? level) =>
        new(level is null ? "{}" : $$"""{"level":"{{level}}"}""", System.Text.Encoding.UTF8, "application/json");

    /// <summary>The head's section A session, started and unanswered.</summary>
    private async Task<(Enrolment Enrolment, Guid SessionId)> StartedAsync(CancellationToken cancellationToken)
    {
        var enrolment = await Enrolment.CreateAsync(api.Factory, cancellationToken, Rosters.Head());
        await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Participation, cancellationToken);
        var assignmentId = await Journeys.AssignmentAsync(enrolment, enrolment.Self, "A", cancellationToken);
        var session = await Journeys.StartAsync(enrolment.Client, assignmentId, cancellationToken);
        return (enrolment, session.Id);
    }

    private Task<int> AuditCountAsync(string action, Guid sessionId, CancellationToken cancellationToken) =>
        api.Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == action && audit.ResourceId == sessionId.ToString(), cancellationToken));

    private Task<int> ScoreCountAsync(Guid sessionId, CancellationToken cancellationToken) =>
        api.Factory.QueryAsync(db => db.ScoreResults.CountAsync(
            score => score.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken));

    [Theory]
    [InlineData("low", "band_low", 2)]
    [InlineData("medium", "band_high", 5)]
    [InlineData("high", "band_high", 5)]
    public async Task One_request_answers_every_question_submits_and_scores_in_the_asked_for_band(string level, string tierCode, int total)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;
        using var client = Demo(enrolment);

        using var response = await SubmitDemoAsync(client, sessionId, level, Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var submission = await response.ReadAsync<SubmissionResponse>(cancellationToken);
        Assert.Equal((sessionId, SubmissionOutcome.Submitted, SessionStatus.Scored), (submission.SessionId, submission.Outcome, submission.Status));
        using var answers = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken);
        Assert.Equal(
            api.Section("A").Items.OrderBy(item => item.DisplayOrder).Select(item => item.Id.Value),
            (await answers.ReadAsync<SessionAnswersResponse>(cancellationToken)).Responses.Select(answer => answer.ItemId));
        using var result = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);
        var scored = await result.ReadAsync<SessionResultResponse>(cancellationToken);
        Assert.Equal((ResultState.Complete, (decimal?)total, tierCode), (scored.State, scored.TotalScore, scored.TierCode));
        var assignmentStatus = await api.Factory.QueryAsync(async db =>
        {
            var session = await db.AssessmentSessions.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == AssessmentSessionId.From(sessionId), cancellationToken);
            return (await db.AssessmentAssignments.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == session.AssessmentAssignmentId, cancellationToken)).Status;
        });
        Assert.Equal(AssignmentStatus.Completed, assignmentStatus);
        Assert.Equal(1, await AuditCountAsync(SubmitDemoAnswers.AuditAction, sessionId, cancellationToken));
        Assert.Equal(1, await AuditCountAsync("sessions.session.submitted", sessionId, cancellationToken));
    }

    [Fact]
    public async Task A_retry_with_the_same_key_answers_as_the_first_did_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;
        using var client = Demo(enrolment);
        var key = Guid.NewGuid();

        using var first = await SubmitDemoAsync(client, sessionId, "high", key, cancellationToken);
        using var retry = await SubmitDemoAsync(client, sessionId, "high", key, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var (once, again) = (await first.ReadAsync<SubmissionResponse>(cancellationToken), await retry.ReadAsync<SubmissionResponse>(cancellationToken));
        Assert.Equal(once, again);
        Assert.Equal(1, await ScoreCountAsync(sessionId, cancellationToken));
        Assert.Equal(1, await AuditCountAsync(SubmitDemoAnswers.AuditAction, sessionId, cancellationToken));
    }

    [Fact]
    public async Task A_session_submitted_by_another_request_answers_409_and_keeps_its_answers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(api.Factory, cancellationToken, Rosters.Head());
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var client = Demo(enrolment);

        using var response = await SubmitDemoAsync(client, sessionId, "high", Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sessions.not_in_progress", await response.ProblemCodeAsync(cancellationToken));
        using var result = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);
        Assert.Equal("band_low", (await result.ReadAsync<SessionResultResponse>(cancellationToken)).TierCode);
        Assert.Equal(0, await AuditCountAsync(SubmitDemoAnswers.AuditAction, sessionId, cancellationToken));
    }

    [Theory]
    [InlineData("extreme", true, "level")]
    [InlineData(null, true, "level")]
    [InlineData("high", false, "idempotencyKey")]
    public async Task An_unknown_level_or_a_missing_key_is_refused_and_nothing_is_saved(string? level, bool withKey, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;
        using var client = Demo(enrolment);

        using var response = await SubmitDemoAsync(client, sessionId, level, withKey ? Guid.NewGuid() : null, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, (await response.ReadAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(cancellationToken)).Errors.Keys);
        using var answers = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken);
        Assert.Empty((await answers.ReadAsync<SessionAnswersResponse>(cancellationToken)).Responses);
    }

    [Fact]
    public async Task Switched_off_the_endpoint_answers_404_and_saves_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;

        using var response = await SubmitDemoAsync(enrolment.Client, sessionId, "high", Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("demo_tools.disabled", await response.ProblemCodeAsync(cancellationToken));
        using var answers = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken);
        Assert.Empty((await answers.ReadAsync<SessionAnswersResponse>(cancellationToken)).Responses);
    }

    [Fact]
    public async Task Another_household_gets_404_staff_get_403_and_nobody_signed_in_gets_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;
        using var stranger = await Enrolment.CreateAsync(api.Factory, cancellationToken, Rosters.Head());
        using var strangerClient = Demo(stranger);
        using var staff = _demo.As(ApiClients.NewSubject("staff"), Roles.SystemAdministrator);
        using var anonymous = _demo.As(null);

        using var asStranger = await SubmitDemoAsync(strangerClient, sessionId, "high", Guid.NewGuid(), cancellationToken);
        using var asStaff = await SubmitDemoAsync(staff, sessionId, "high", Guid.NewGuid(), cancellationToken);
        using var asNobody = await SubmitDemoAsync(anonymous, sessionId, "high", Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, asStranger.StatusCode);
        Assert.Equal("sessions.session_not_found", await asStranger.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asNobody.StatusCode);
        Assert.Equal(0, await ScoreCountAsync(sessionId, cancellationToken));
        Assert.Equal(0, await AuditCountAsync(SubmitDemoAnswers.AuditAction, sessionId, cancellationToken));
    }

    [Fact]
    public async Task A_session_without_approved_scoring_rules_answers_409_and_saves_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(api.Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var parent = enrolment.Aged(68);
        var sessionId = await Journeys.StartedSessionAsync(enrolment, api, parent, "B", cancellationToken);
        using var client = Demo(enrolment);

        using var response = await SubmitDemoAsync(client, sessionId, "low", Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("scoring.policy_not_approved", await response.ProblemCodeAsync(cancellationToken));
        using var answers = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken);
        Assert.Empty((await answers.ReadAsync<SessionAnswersResponse>(cancellationToken)).Responses);
    }

    [Fact]
    public async Task After_the_wave_closed_it_answers_409_expired_and_nothing_is_saved_or_scored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;
        await using var later = _demo.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(api.Wave.ClosesAt.AddDays(1)))));
        using var client = later.As(enrolment.Subject, Roles.Participant);

        using var response = await SubmitDemoAsync(client, sessionId, "high", Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("assignments.assignment_expired", await response.ProblemCodeAsync(cancellationToken));
        using var answers = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken);
        Assert.Empty((await answers.ReadAsync<SessionAnswersResponse>(cancellationToken)).Responses);
        Assert.Equal(0, await ScoreCountAsync(sessionId, cancellationToken));
    }

    [Fact]
    public async Task The_plan_for_a_rule_set_and_level_is_made_once_and_reused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var plans = _demo.Services.GetRequiredService<DemoAnswerPlanCache>();
        var (first, firstSession) = await StartedAsync(cancellationToken);
        var (second, secondSession) = await StartedAsync(cancellationToken);
        using var __ = first;
        using var ___ = second;

        using var one = await SubmitDemoAsync(Demo(first), firstSession, "high", Guid.NewGuid(), cancellationToken);
        var afterOne = plans.Count;
        using var two = await SubmitDemoAsync(Demo(second), secondSession, "high", Guid.NewGuid(), cancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (one.StatusCode, two.StatusCode));
        Assert.Equal((1, 1), (afterOne, plans.Count));
    }
}
