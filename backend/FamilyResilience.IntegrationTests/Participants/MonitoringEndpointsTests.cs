using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

[Collection(ParticipantApiCollection.Name)]
public sealed class MonitoringEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    [Fact]
    public async Task Participation_shows_a_households_progress_found_by_its_reference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68), Rosters.Child(9));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        var subject = ApiClients.NewSubject("program-administrator");
        using var admin = Factory.AsProgramAdministrator(subject);

        using var response = await admin.GetAsync($"/api/v1/admin/participation?reference={enrolment.Household.Reference}", cancellationToken);

        var row = Assert.Single((await response.ReadAsync<PagedResponse<ParticipationRowResponse>>(cancellationToken)).Items);
        Assert.Equal((enrolment.Household.Id, HouseholdStatus.Active, 3), (row.HouseholdId, row.Status, row.ActiveMemberCount));
        Assert.NotNull(row.RosterConfirmedAt);
        Assert.Equal(new AssignmentCountsResponse(1, 0, 1, 0, 0), row.Assignments);
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.participation.read" && audit.ActorId == subject, cancellationToken)));
    }

    [Fact]
    public async Task Participation_filters_by_status_and_roster_confirmation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var withdrawn = await Enrolment.CreateAsync(Factory, cancellationToken);
        using var left = await withdrawn.Client.PatchJsonAsync($"/api/v1/households/{withdrawn.Household.Id}", new { status = "withdrawn" }, cancellationToken);
        using var admin = Factory.AsProgramAdministrator();

        using var active = await admin.GetAsync($"/api/v1/admin/participation?status=active&reference={withdrawn.Household.Reference}", cancellationToken);
        using var gone = await admin.GetAsync($"/api/v1/admin/participation?status=withdrawn&rosterConfirmed=false&reference={withdrawn.Household.Reference}", cancellationToken);

        Assert.Equal(0, (await active.ReadAsync<PagedResponse<ParticipationRowResponse>>(cancellationToken)).TotalCount);
        Assert.Equal(withdrawn.Household.Id, Assert.Single((await gone.ReadAsync<PagedResponse<ParticipationRowResponse>>(cancellationToken)).Items).HouseholdId);
    }

    [Fact]
    public async Task Assessments_show_progress_but_never_answers_or_scores()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var admin = Factory.AsProgramAdministrator();

        using var response = await admin.GetAsync(
            $"/api/v1/admin/assessments?householdId={enrolment.Household.Id}&waveId={api.Wave.Id.Value}&instrumentCode={api.Instrument.Code}", cancellationToken);
        using var completedOnly = await admin.GetAsync(
            $"/api/v1/admin/assessments?householdId={enrolment.Household.Id}&status=completed", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var hidden in new[] { "tierCode", "totalScore", "responses", "optionId", "categoryScores" })
        {
            Assert.DoesNotContain(hidden, body, StringComparison.Ordinal);
        }

        var rows = (await response.ReadAsync<PagedResponse<AssessmentRowResponse>>(cancellationToken)).Items;
        var answered = Assert.Single(rows, row => row.SectionCode == "A");
        Assert.Equal((AssignmentStatus.Completed, (SessionStatus?)SessionStatus.Scored, true, api.Wave.Code),
            (answered.AssignmentStatus, answered.SessionStatus, answered.Scored, answered.WaveCode));
        var waiting = Assert.Single(rows, row => row.SectionCode == "B");
        Assert.Equal((AssignmentStatus.Assigned, (SessionStatus?)null, false), (waiting.AssignmentStatus, waiting.SessionStatus, waiting.Scored));
        Assert.Equal(answered.AssignmentId, Assert.Single((await completedOnly.ReadAsync<PagedResponse<AssessmentRowResponse>>(cancellationToken)).Items).AssignmentId);
    }

    [Fact]
    public async Task Assessments_filter_by_instrument_version_and_by_assigned_days_inclusive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        await Journeys.AssignmentAsync(enrolment, enrolment.Self, "A", cancellationToken);
        using var admin = Factory.AsProgramAdministrator();
        var day = DateOnly.FromDateTime(TestData.Now.UtcDateTime);

        async Task<int> CountAsync(string filters)
        {
            using var response = await admin.GetAsync(
                $"/api/v1/admin/assessments?householdId={enrolment.Household.Id}{filters}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadAsync<PagedResponse<AssessmentRowResponse>>(cancellationToken)).TotalCount;
        }

        var all = await CountAsync(string.Empty);
        Assert.True(all > 0);
        Assert.Equal(all, await CountAsync($"&instrumentCode={api.Instrument.Code}&versionNumber={api.Version.VersionNumber}"));
        Assert.Equal(0, await CountAsync($"&instrumentCode={api.Instrument.Code}&versionNumber={api.Version.VersionNumber + 1}"));
        Assert.Equal(all, await CountAsync($"&assignedFrom={day:yyyy-MM-dd}&assignedTo={day:yyyy-MM-dd}"));
        Assert.Equal(0, await CountAsync($"&assignedFrom={day.AddDays(1):yyyy-MM-dd}"));
        Assert.Equal(0, await CountAsync($"&assignedTo={day.AddDays(-1):yyyy-MM-dd}"));
    }

    [Theory]
    [InlineData("/api/v1/admin/participation?status=sleeping", "status")]
    [InlineData("/api/v1/admin/participation?reference=not-a-reference", "reference")]
    [InlineData("/api/v1/admin/participation?pageSize=101", "pageSize")]
    [InlineData("/api/v1/admin/assessments?instrumentCode=Bad%20Code", "instrumentCode")]
    [InlineData("/api/v1/admin/assessments?waveId=00000000-0000-0000-0000-000000000000", "waveId")]
    [InlineData("/api/v1/admin/assessments?status=done", "status")]
    [InlineData("/api/v1/admin/assessments?versionNumber=1", "versionNumber")]
    [InlineData("/api/v1/admin/assessments?instrumentCode=some_code&versionNumber=0", "versionNumber")]
    [InlineData("/api/v1/admin/assessments?assignedFrom=2026-10-12&assignedTo=2026-10-11", "assignedFrom")]
    [InlineData("/api/v1/admin/assessments?assignedFrom=not-a-date", "assignedFrom")]
    [InlineData("/api/v1/admin/assessments?assignedTo=2026-13-01", "assignedTo")]
    [InlineData("/api/v1/admin/assessments?assignedTo=9999-12-31", "assignedTo")]
    [InlineData("/api/v1/admin/assessments?assignedFrom=1999-12-31", "assignedFrom")]
    public async Task Unknown_filters_return_400_keyed_by_the_filter(string url, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = Factory.AsProgramAdministrator();

        using var response = await admin.GetAsync(url, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.SystemAdministrator)]
    [InlineData(Roles.Participant)]
    [InlineData(Roles.SupportCoordinator)]
    public async Task Only_program_administrators_may_monitor(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(ApiClients.NewSubject("caller"), role);

        using var participation = await client.GetAsync("/api/v1/admin/participation", cancellationToken);
        using var assessments = await client.GetAsync("/api/v1/admin/assessments", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, participation.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, assessments.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        using var client = Factory.As(null);

        using var response = await client.GetAsync("/api/v1/admin/participation", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A 400 never repeats what the client sent: the value could be markup, or something a log should not hold.
    [Theory]
    [InlineData("/api/v1/admin/participation?status=", "zz-echo-probe-7c1f")]
    [InlineData("/api/v1/admin/participation?status=", "%3Cscript%3Ealert(1)%3C%2Fscript%3E")]
    [InlineData("/api/v1/admin/assessments?status=", "1")]
    public async Task An_unknown_enum_filter_returns_a_fixed_message_that_does_not_repeat_the_value(string url, string value)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = Factory.AsProgramAdministrator();

        using var response = await admin.GetAsync(url + value, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var messages = document.RootElement.GetProperty("errors").GetProperty("status").EnumerateArray().Select(message => message.GetString()).ToList();
        Assert.Equal(["The value is not one of the allowed values."], messages);
        Assert.DoesNotContain(Uri.UnescapeDataString(value), string.Join(' ', messages), StringComparison.Ordinal);
        Assert.DoesNotContain("zz-echo-probe", body, StringComparison.Ordinal);
        Assert.DoesNotContain("script", body, StringComparison.OrdinalIgnoreCase);
    }
}
