using System.Net;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Demonstration;

/// <summary>
/// A demonstration's "new check-in" (D-144) on the demonstration database, where the household assessment and its scoring
/// are approved locally: each tap gives the caller's household a round of its own, so a second result adds a point to its
/// trend chart. It has a demonstration database of its own, because its households and rounds would change the counts
/// the shared one's tests read; every test enrols a household of its own with a new subject.
/// </summary>
public sealed class DemoCheckInTests(DemonstrationApi api) : IClassFixture<DemonstrationApi>, IAsyncDisposable
{
    private const string CheckIn = "/api/v1/assignments/demo-check-in";

    private readonly WebApplicationFactory<Program> _demo =
        api.WithWebHostBuilder(builder => builder.UseSetting(DemoToolsSettings.EnabledKey, "true"));

    public async ValueTask DisposeAsync() => await _demo.DisposeAsync();

    /// <summary>A new head of household with the roster confirmed and participation consent: assigned the open wave.</summary>
    private async Task<(HttpClient Client, Guid HouseholdId, Guid Self, Guid AssignmentId)> EnrolledAsync(CancellationToken cancellationToken)
    {
        var client = _demo.As(ApiClients.NewSubject("demo-check-in"), Roles.Participant);
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse());
        using var confirmed = await client.PostJsonAsync($"/api/v1/households/{household.Id}/confirm-roster", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmation = await confirmed.ReadAsync<RosterConfirmationResponse>(cancellationToken);
        Assert.Equal(DemonstrationDataSeeder.WaveTwoCode, confirmation.WaveCode);
        var self = (await client.MembersAsync(household.Id, cancellationToken)).Single(member => member.IsCaller).Id;
        await client.GrantConsentAsync(self, ConsentType.Participation, cancellationToken);
        return (client, household.Id, self, Assert.Single(confirmation.Assignments).AssignmentId);
    }

    private static async Task<SubmissionResponse> AnswerAsync(HttpClient client, Guid assignmentId, string level, CancellationToken cancellationToken)
    {
        var session = await Journeys.StartAsync(client, assignmentId, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{session.Id}/demo-submission")
        {
            Content = new StringContent($$"""{"level":"{{level}}"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SubmissionResponse>(cancellationToken);
    }

    private static async Task<DemoCheckInResponse> OpenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync(CheckIn, new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<DemoCheckInResponse>(cancellationToken);
    }

    [Fact]
    public async Task Each_new_check_in_is_a_round_of_its_own_for_this_household_and_each_result_adds_a_point()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, householdId, self, firstAssignment) = await EnrolledAsync(cancellationToken);
        using var _ = client;
        var wavesBefore = await api.QueryAsync(db => db.Waves.CountAsync(cancellationToken));
        var waveTwoBefore = await api.QueryAsync(db => db.AssessmentAssignments.CountAsync(
            assignment => assignment.WaveId == api.WaveTwo.Id, cancellationToken));

        var first = await AnswerAsync(client, firstAssignment, "low", cancellationToken);
        var opened = await OpenAsync(client, cancellationToken);
        var second = await AnswerAsync(client, opened.AssignmentId, "high", cancellationToken);
        var reopened = await OpenAsync(client, cancellationToken);
        var third = await AnswerAsync(client, reopened.AssignmentId, "medium", cancellationToken);
        using var trendResponse = await client.GetAsync("/api/v1/me/resilience-trend", cancellationToken);

        Assert.True(opened.Opened);
        Assert.Equal(OpenDemoCheckIn.WaveCodeFor(wavesBefore + 1), opened.WaveCode);
        Assert.Equal(OpenDemoCheckIn.WaveCodeFor(wavesBefore + 2), reopened.WaveCode);
        var wave = await api.QueryAsync(db => db.Waves.AsNoTracking().SingleAsync(candidate => candidate.Code == opened.WaveCode, cancellationToken));
        // Never open, so roster confirmation everywhere else still finds the one open wave.
        Assert.Equal(WaveStatus.Planned, wave.Status);
        var inTheRound = await api.QueryAsync(db => db.AssessmentAssignments.AsNoTracking()
            .Where(assignment => assignment.WaveId == wave.Id).ToListAsync(cancellationToken));
        var only = Assert.Single(inTheRound);
        Assert.Equal((self, AssignmentStatus.Completed), (only.FamilyMemberId.Value, only.Status));
        Assert.Equal(
            waveTwoBefore,
            await api.QueryAsync(db => db.AssessmentAssignments.CountAsync(assignment => assignment.WaveId == api.WaveTwo.Id, cancellationToken)));
        var trend = await trendResponse.ReadAsync<ResilienceTrendResponse>(cancellationToken);
        Assert.Equal(
            [(first.SessionId, DemonstrationDataSeeder.WaveTwoCode), (second.SessionId, opened.WaveCode), (third.SessionId, reopened.WaveCode)],
            trend.Points.Select(point => (point.SessionId, point.WaveCode)));
        Assert.Equal(["vulnerable", "flourishing", "transitional"], trend.Points.Select(point => point.TierCode));
        Assert.Equal(2, await api.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == OpenDemoCheckIn.AuditAction && audit.ResourceId == householdId.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task A_check_in_still_open_is_returned_again_instead_of_a_new_round()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, _, _, assignmentId) = await EnrolledAsync(cancellationToken);
        using var __ = client;
        var wavesBefore = await api.QueryAsync(db => db.Waves.CountAsync(cancellationToken));

        var open = await OpenAsync(client, cancellationToken);
        var session = await Journeys.StartAsync(client, assignmentId, cancellationToken);
        var again = await OpenAsync(client, cancellationToken);

        Assert.Equal((assignmentId, false, DemonstrationDataSeeder.WaveTwoCode, (Guid?)null), (open.AssignmentId, open.Opened, open.WaveCode, open.ActiveSessionId));
        Assert.Equal((assignmentId, false, (Guid?)session.Id), (again.AssignmentId, again.Opened, again.ActiveSessionId));
        Assert.Equal(wavesBefore, await api.QueryAsync(db => db.Waves.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task A_household_whose_roster_is_not_confirmed_answers_409_and_no_round_is_made()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _demo.As(ApiClients.NewSubject("demo-check-in"), Roles.Participant);
        await client.CreateHouseholdAsync(cancellationToken, Rosters.Head());
        var wavesBefore = await api.QueryAsync(db => db.Waves.CountAsync(cancellationToken));

        using var response = await client.PostJsonAsync(CheckIn, new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("demo_tools.roster_not_confirmed", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(wavesBefore, await api.QueryAsync(db => db.Waves.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Nobody_but_a_household_manager_may_and_the_switched_off_endpoint_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var notEnrolled = _demo.As(ApiClients.NewSubject("demo-check-in"), Roles.Participant);
        using var member = _demo.As(DemonstrationDataSeeder.MemberSubject, Roles.Participant);
        using var staff = _demo.As(ApiClients.NewSubject("staff"), Roles.SystemAdministrator);
        using var anonymous = _demo.As(null);
        using var switchedOff = api.As(DemonstrationDataSeeder.LeadSubject, Roles.Participant);
        var wavesBefore = await api.QueryAsync(db => db.Waves.CountAsync(cancellationToken));

        using var asNotEnrolled = await notEnrolled.PostJsonAsync(CheckIn, new { }, cancellationToken);
        using var asMember = await member.PostJsonAsync(CheckIn, new { }, cancellationToken);
        using var asStaff = await staff.PostJsonAsync(CheckIn, new { }, cancellationToken);
        using var asNobody = await anonymous.PostJsonAsync(CheckIn, new { }, cancellationToken);
        using var off = await switchedOff.PostJsonAsync(CheckIn, new { }, cancellationToken);

        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound),
            (asNotEnrolled.StatusCode, asMember.StatusCode, asStaff.StatusCode, asNobody.StatusCode, off.StatusCode));
        Assert.Equal("households.household_not_found", await asMember.ProblemCodeAsync(cancellationToken));
        Assert.Equal("demo_tools.disabled", await off.ProblemCodeAsync(cancellationToken));
        Assert.Equal(wavesBefore, await api.QueryAsync(db => db.Waves.CountAsync(cancellationToken)));
    }
}
