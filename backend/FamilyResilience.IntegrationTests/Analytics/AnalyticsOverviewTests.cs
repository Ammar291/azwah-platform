using System.Net;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Analytics;

[Collection(AnalyticsApiCollection.Name)]
public sealed class AnalyticsOverviewTests(AnalyticsApi api)
{
    private ApiFactory Factory => api.Factory;

    private async Task<AnalyticsOverviewResponse> OverviewAsync(string query, CancellationToken cancellationToken)
    {
        using var client = Factory.AsResearcher();
        using var response = await client.GetAsync($"/api/v1/admin/analytics/overview{query}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<AnalyticsOverviewResponse>(cancellationToken);
    }

    // The withdrawn household's two members are still Active rows; they are not active members of the study.
    [Fact]
    public async Task Enrolment_counts_every_household_and_the_active_members_of_active_households()
    {
        var overview = await OverviewAsync(string.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(new EnrollmentCounts(30, 29, 1, 0, 29), overview.Enrollment);
        Assert.Equal(AnalyticsSettings.DefaultMinimumGroupSize, overview.MinimumGroupSize);
        Assert.Equal(31, await Factory.QueryAsync(db => db.Set<FamilyMember>().CountAsync(
            member => member.Status == MemberStatus.Active, TestContext.Current.CancellationToken)));
    }

    // Wave four: four members aged 25, each holding three assignments. D-58 suppresses cells of fewer than 10 people.
    [Fact]
    public async Task A_cell_of_twelve_assignments_held_by_four_people_is_suppressed()
    {
        var overview = await OverviewAsync($"?waveId={api.WaveFour.Id.Value}", TestContext.Current.CancellationToken);

        Assert.Equal(new AssignmentTotalsResponse(12, 0, 12, 0, 0, 0, 0, 0m), overview.Assignments);
        Assert.Equal(new DemographicRowResponse("18_29", null, 0, null, true), overview.Demographics.ByAgeBand[0]);
        Assert.All(overview.Demographics.ByAgeBand, row => Assert.True(row.Suppressed));
        var student = Assert.Single(overview.Demographics.ByEmploymentStatus, row => row.Group == "student");
        Assert.Equal(new DemographicRowResponse("student", null, 0, null, true), student);
    }

    [Fact]
    public async Task With_suppression_off_the_same_cell_still_shows_its_twelve_assignments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var unsuppressed = Factory.WithWebHostBuilder(builder => builder.UseSetting("Analytics:MinimumGroupSize", "1"));
        using var client = unsuppressed.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ApiClients.NewSubject("researcher"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Researcher);

        using var response = await client.GetAsync($"/api/v1/admin/analytics/overview?waveId={api.WaveFour.Id.Value}", cancellationToken);

        var overview = await response.ReadAsync<AnalyticsOverviewResponse>(cancellationToken);
        Assert.Equal(new DemographicRowResponse("18_29", 12, 0, 0m, false), overview.Demographics.ByAgeBand[0]);
        Assert.Equal(
            new DemographicRowResponse("student", 12, 0, 0m, false),
            Assert.Single(overview.Demographics.ByEmploymentStatus, row => row.Group == "student"));
    }

    // Four people are under the minimum of 4 + 1 and at the minimum of 4: the decision is on people, not on 12.
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(12, true)]
    public async Task The_minimum_is_compared_with_the_people_behind_a_cell(int minimum, bool suppressed)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = Factory.WithWebHostBuilder(
            builder => builder.UseSetting("Analytics:MinimumGroupSize", minimum.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ApiClients.NewSubject("researcher"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Researcher);

        using var response = await client.GetAsync($"/api/v1/admin/analytics/overview?waveId={api.WaveFour.Id.Value}", cancellationToken);

        var overview = await response.ReadAsync<AnalyticsOverviewResponse>(cancellationToken);
        Assert.Equal(suppressed, overview.Demographics.ByAgeBand[0].Suppressed);
        Assert.Equal(suppressed ? null : 12, overview.Demographics.ByAgeBand[0].Total);
    }

    [Fact]
    public async Task A_wave_counts_its_assignments_starts_completions_and_completion_rate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var waveOne = await OverviewAsync($"?waveId={api.WaveOne.Id.Value}", cancellationToken);
        var waveTwo = await OverviewAsync($"?waveId={api.WaveTwo.Id.Value}", cancellationToken);

        Assert.Equal(new AssignmentTotalsResponse(24, 21, 3, 0, 21, 0, 0, 0.875m), waveOne.Assignments);
        var version = Assert.Single(waveOne.ByVersion);
        Assert.Equal((api.Version.VersionNumber, (string?)null, 24, 21, (decimal?)0.875m),
            (version.VersionNumber, version.SectionCode, version.Total, version.Completed, version.CompletionRate));
        Assert.Equal(new AssignmentTotalsResponse(1, 0, 1, 0, 0, 0, 0, 0m), waveTwo.Assignments);
    }

    [Fact]
    public async Task Completions_are_counted_by_month_with_empty_months_as_zero()
    {
        var overview = await OverviewAsync($"?waveId={api.WaveOne.Id.Value}", TestContext.Current.CancellationToken);

        Assert.Equal((new DateOnly(2025, 10, 11), new DateOnly(2026, 10, 11), TrendInterval.Month), (overview.From, overview.To, overview.Interval));
        Assert.Equal(13, overview.CompletionTrend.Count);
        Assert.Equal(new TrendPointResponse(new DateOnly(2026, 10, 1), 21), overview.CompletionTrend[^1]);
        Assert.All(overview.CompletionTrend.SkipLast(1), point => Assert.Equal(0, point.Completed));
    }

    [Fact]
    public async Task Completions_can_be_counted_by_iso_week_within_a_window()
    {
        var overview = await OverviewAsync(
            $"?waveId={api.WaveOne.Id.Value}&from=2026-10-01&to=2026-10-11&interval=week", TestContext.Current.CancellationToken);

        Assert.Equal(
            [new TrendPointResponse(new DateOnly(2026, 9, 28), 0), new TrendPointResponse(new DateOnly(2026, 10, 5), 21)],
            overview.CompletionTrend);
    }

    [Fact]
    public async Task Small_age_bands_are_suppressed_with_a_complementary_cell()
    {
        var overview = await OverviewAsync($"?waveId={api.WaveOne.Id.Value}", TestContext.Current.CancellationToken);

        Assert.Equal(TestData.Today, overview.Demographics.AgeAsOf);
        Assert.Equal(
            [
                new DemographicRowResponse("18_29", 0, 0, null, false),
                new DemographicRowResponse("30_44", 12, 12, 1m, false),
                new DemographicRowResponse("45_59", null, null, null, true),
                new DemographicRowResponse("60_plus", null, null, null, true),
            ],
            overview.Demographics.ByAgeBand);
    }

    [Fact]
    public async Task A_single_small_employment_group_hides_the_next_smallest_too()
    {
        var overview = await OverviewAsync($"?waveId={api.WaveOne.Id.Value}", TestContext.Current.CancellationToken);

        var rows = overview.Demographics.ByEmploymentStatus.ToDictionary(row => row.Group);
        Assert.True(rows["retired"].Suppressed);
        Assert.True(rows["employed"].Suppressed);
        Assert.Equal(new DemographicRowResponse("student", 0, 0, null, false), rows["student"]);
        Assert.Contains("not_recorded", rows.Keys);
    }

    [Fact]
    public async Task With_suppression_off_every_cell_is_shown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var unsuppressed = Factory.WithWebHostBuilder(builder => builder.UseSetting("Analytics:MinimumGroupSize", "1"));
        using var client = unsuppressed.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ApiClients.NewSubject("researcher"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Researcher);

        using var response = await client.GetAsync($"/api/v1/admin/analytics/overview?waveId={api.WaveOne.Id.Value}", cancellationToken);

        var overview = await response.ReadAsync<AnalyticsOverviewResponse>(cancellationToken);
        Assert.Equal(1, overview.MinimumGroupSize);
        Assert.Equal(new DemographicRowResponse("45_59", 10, 7, 0.7m, false), overview.Demographics.ByAgeBand[2]);
    }

    [Fact]
    public async Task The_overview_returns_no_individual_values_and_each_read_is_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("researcher");
        using var client = Factory.AsResearcher(subject);

        using var response = await client.GetAsync("/api/v1/admin/analytics/overview", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var individual in new[] { "householdId", "memberId", "sessionId", "reference", "birthYear", "totalScore", "tierCode", "tiers", "scoreDistributions" })
        {
            Assert.DoesNotContain(individual, body, StringComparison.Ordinal);
        }

        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.overview.read" && audit.ActorId == subject, cancellationToken)));
    }

    [Theory]
    [InlineData("?from=2026-10-11&to=2026-10-01", "to")]
    [InlineData("?from=2020-01-01&to=2026-10-01", "to")]
    [InlineData("?interval=day", "interval")]
    [InlineData("?waveId=00000000-0000-0000-0000-000000000000", "waveId")]
    [InlineData("?from=yesterday", "from")]
    [InlineData("?to=9999-12-31", "to")]
    [InlineData("?to=0001-03-01", "to")]
    [InlineData("?from=2001-01-01&interval=week", "to")]
    public async Task An_invalid_filter_returns_400_keyed_by_the_filter(string query, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();

        using var response = await client.GetAsync($"/api/v1/admin/analytics/overview{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData(Roles.Researcher, HttpStatusCode.OK)]
    [InlineData(Roles.ProgramAdministrator, HttpStatusCode.OK)]
    [InlineData(Roles.SystemAdministrator, HttpStatusCode.Forbidden)]
    [InlineData(Roles.SupportCoordinator, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Participant, HttpStatusCode.Forbidden)]
    public async Task Only_researchers_and_program_administrators_read_analytics(string role, HttpStatusCode expected)
    {
        using var client = Factory.As(ApiClients.NewSubject("caller"), role);

        using var response = await client.GetAsync("/api/v1/admin/analytics/overview", TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        using var client = Factory.As(null);

        using var response = await client.GetAsync("/api/v1/admin/analytics/overview", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
