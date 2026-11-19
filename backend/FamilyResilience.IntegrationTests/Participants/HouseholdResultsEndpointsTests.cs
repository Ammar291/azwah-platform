using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>The household results list (D-104): system administrators only, the latest result per session, audited.</summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class HouseholdResultsEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    [Fact]
    public async Task System_administrators_see_a_households_latest_total_and_tier_found_by_its_reference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        var subject = ApiClients.NewSubject("system-administrator");
        using var admin = Factory.As(subject, Roles.SystemAdministrator);

        using var response = await admin.GetAsync(
            $"/api/v1/admin/results?reference={enrolment.Household.Reference}&waveId={api.Wave.Id.Value}&instrumentCode={api.Instrument.Code}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsync<PagedResponse<HouseholdResultRowResponse>>(cancellationToken);
        var row = Assert.Single(page.Items);
        Assert.Equal(
            (sessionId, enrolment.Household.Id, enrolment.Household.Reference, api.Wave.Code, api.Instrument.Code, "A"),
            (row.SessionId, row.HouseholdId, row.HouseholdReference, row.WaveCode, row.InstrumentCode, row.SectionCode));
        Assert.Equal((ScoreStatus.Complete, (decimal?)2m, "band_low"), (row.State, row.TotalScore, row.TierCode));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var hidden in new[] { "responses", "optionId", "memberId", "categoryScores" })
        {
            Assert.DoesNotContain(hidden, body, StringComparison.Ordinal);
        }

        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "scoring.household_results.read" && audit.ActorId == subject, cancellationToken)));
    }

    [Fact]
    public async Task Filters_narrow_the_list_and_a_household_without_results_has_none()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var unanswered = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        using var admin = Factory.AsSystemAdministrator();

        async Task<int> CountAsync(string query)
        {
            using var response = await admin.GetAsync($"/api/v1/admin/results?{query}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadAsync<PagedResponse<HouseholdResultRowResponse>>(cancellationToken)).TotalCount;
        }

        Assert.Equal(1, await CountAsync($"reference={enrolment.Household.Reference}"));
        Assert.Equal(0, await CountAsync($"reference={enrolment.Household.Reference}&instrumentCode=another_instrument"));
        Assert.Equal(0, await CountAsync($"reference={enrolment.Household.Reference}&waveId={Guid.NewGuid()}"));
        Assert.Equal(0, await CountAsync($"reference={unanswered.Household.Reference}"));
    }

    [Theory]
    [InlineData("/api/v1/admin/results?reference=not-a-reference", "reference")]
    [InlineData("/api/v1/admin/results?instrumentCode=Bad%20Code", "instrumentCode")]
    [InlineData("/api/v1/admin/results?waveId=00000000-0000-0000-0000-000000000000", "waveId")]
    [InlineData("/api/v1/admin/results?pageSize=101", "pageSize")]
    [InlineData("/api/v1/admin/results?page=0", "page")]
    public async Task Invalid_filters_return_400_keyed_by_the_filter(string url, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = Factory.AsSystemAdministrator();

        using var response = await admin.GetAsync(url, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData(Roles.ProgramAdministrator)]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.SupportCoordinator)]
    [InlineData(Roles.Participant)]
    public async Task Only_system_administrators_may_read_household_results(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(ApiClients.NewSubject("caller"), role);

        using var response = await client.GetAsync("/api/v1/admin/results", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        using var client = Factory.As(null);

        using var response = await client.GetAsync("/api/v1/admin/results", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
