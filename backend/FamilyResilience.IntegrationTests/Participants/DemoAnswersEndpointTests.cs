using System.Net;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// The demonstration answers (D-130) on a host with <c>DemoTools:Enabled</c>, derived from the participant host so it
/// shares its approved section A rules (totals 2–6: band_low 2–3, band_high 4–6). The shared host keeps them off.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class DemoAnswersEndpointTests(ParticipantApi api) : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _demo =
        api.Factory.WithWebHostBuilder(builder => builder.UseSetting(DemoToolsSettings.EnabledKey, "true"));

    public async ValueTask DisposeAsync() => await _demo.DisposeAsync();

    private HttpClient Demo(Enrolment enrolment) => _demo.As(enrolment.Subject, Roles.Participant);

    private static string PathOf(Guid sessionId, string? level = null) =>
        $"/api/v1/sessions/{sessionId}/demo-answers" + (level is null ? string.Empty : $"?level={level}");

    /// <summary>The head's section A session, started and unanswered.</summary>
    private async Task<(Enrolment Enrolment, Guid SessionId)> StartedAsync(CancellationToken cancellationToken)
    {
        var enrolment = await Enrolment.CreateAsync(api.Factory, cancellationToken, Rosters.Head());
        await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Participation, cancellationToken);
        var assignmentId = await Journeys.AssignmentAsync(enrolment, enrolment.Self, "A", cancellationToken);
        var session = await Journeys.StartAsync(enrolment.Client, assignmentId, cancellationToken);
        return (enrolment, session.Id);
    }

    [Theory]
    [InlineData("low", "band_low", 2)]
    [InlineData("medium", "band_high", 5)]
    [InlineData("high", "band_high", 5)]
    public async Task Saved_and_submitted_the_answers_score_in_the_asked_for_band(string level, string tierCode, int total)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;
        using var client = Demo(enrolment);

        using var planned = await client.GetAsync(PathOf(sessionId, level), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, planned.StatusCode);
        var plan = await planned.ReadAsync<DemoAnswersResponse>(cancellationToken);
        Assert.Equal(
            api.Section("A").Items.OrderBy(item => item.DisplayOrder).Select(item => item.Id.Value),
            plan.Responses.Select(answer => answer.ItemId));
        var untouched = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken);
        Assert.Empty((await untouched.ReadAsync<SessionAnswersResponse>(cancellationToken)).Responses);
        untouched.Dispose();

        await Journeys.SaveAsync(enrolment.Client, sessionId, [.. plan.Responses], cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var result = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var scored = await result.ReadAsync<SessionResultResponse>(cancellationToken);
        Assert.Equal((ResultState.Complete, (decimal?)total, tierCode), (scored.State, scored.TotalScore, scored.TierCode));
        Assert.Equal(1, await api.Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "sessions.demo_answers.planned" && audit.ResourceId == sessionId.ToString(), cancellationToken)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("extreme")]
    [InlineData("HIGH")]
    public async Task A_missing_or_unknown_level_is_refused(string? level)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;
        using var client = Demo(enrolment);

        using var response = await client.GetAsync(PathOf(sessionId, level), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("level", (await response.ReadAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(cancellationToken)).Errors.Keys);
    }

    [Fact]
    public async Task Switched_off_the_endpoint_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;

        using var response = await enrolment.Client.GetAsync(PathOf(sessionId, "high"), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("demo_tools.disabled", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Another_household_gets_404_and_staff_get_403_and_nobody_signed_in_gets_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (enrolment, sessionId) = await StartedAsync(cancellationToken);
        using var _ = enrolment;
        using var stranger = await Enrolment.CreateAsync(api.Factory, cancellationToken, Rosters.Head());
        using var strangerClient = Demo(stranger);
        using var staff = _demo.As(ApiClients.NewSubject("staff"), Roles.SystemAdministrator);
        using var anonymous = _demo.As(null);

        using var asStranger = await strangerClient.GetAsync(PathOf(sessionId, "high"), cancellationToken);
        using var asStaff = await staff.GetAsync(PathOf(sessionId, "high"), cancellationToken);
        using var asNobody = await anonymous.GetAsync(PathOf(sessionId, "high"), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, asStranger.StatusCode);
        Assert.Equal("sessions.session_not_found", await asStranger.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asNobody.StatusCode);
        Assert.Equal(0, await api.Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "sessions.demo_answers.planned" && audit.ResourceId == sessionId.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task A_session_without_approved_scoring_rules_answers_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(api.Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var parent = enrolment.Aged(68);
        await enrolment.Client.GrantConsentAsync(parent, ConsentType.Participation, cancellationToken);
        var assignmentId = await Journeys.AssignmentAsync(enrolment, parent, "B", cancellationToken);
        var session = await Journeys.StartAsync(enrolment.Client, assignmentId, cancellationToken);
        using var client = Demo(enrolment);

        using var response = await client.GetAsync(PathOf(session.Id, "low"), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("scoring.policy_not_approved", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_submitted_session_answers_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(api.Factory, cancellationToken, Rosters.Head());
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var client = Demo(enrolment);

        using var response = await client.GetAsync(PathOf(sessionId, "high"), cancellationToken);

        Assert.Equal(SessionStatus.Scored, (await submitted.ReadAsync<SubmissionResponse>(cancellationToken)).Status);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sessions.not_in_progress", await response.ProblemCodeAsync(cancellationToken));
    }
}
