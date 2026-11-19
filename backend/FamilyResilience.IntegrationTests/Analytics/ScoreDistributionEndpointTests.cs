using System.Net;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Identity;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Analytics;

/// <summary>
/// Score distributions (D-59, D-62) for researchers only. Program administrators can see when a household was scored,
/// so a distribution read before and after would reveal that household's tier; they get 403.
/// </summary>
[Collection(AnalyticsApiCollection.Name)]
public sealed class ScoreDistributionEndpointTests(AnalyticsApi api)
{
    private ApiFactory Factory => api.Factory;

    private static HttpClient Researcher(WebApplicationFactory<Program> host, string subject)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Researcher);
        return client;
    }

    private static async Task<ScoreDistributionsResponse> ReadAsync(HttpClient client, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/v1/admin/analytics/score-distributions{query}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<ScoreDistributionsResponse>(cancellationToken);
    }

    [Fact]
    public async Task Distributions_count_complete_results_of_approved_rule_sets_with_research_consent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var unsuppressed = Factory.WithWebHostBuilder(builder => builder.UseSetting("Analytics:MinimumGroupSize", "1"));
        using var client = Researcher(unsuppressed, ApiClients.NewSubject("researcher"));

        var read = await ReadAsync(client, $"?waveId={api.WaveOne.Id.Value}", cancellationToken);

        Assert.Equal((1, (Guid?)api.WaveOne.Id.Value), (read.MinimumGroupSize, read.WaveId));
        var distribution = Assert.Single(read.Distributions);
        Assert.Equal(
            (api.Version.VersionNumber, (string?)null, api.Scoring.Code, api.Scoring.VersionNumber, (int?)18, false),
            (distribution.VersionNumber, distribution.SectionCode, distribution.RuleSetCode, distribution.RuleSetVersion, distribution.Total, distribution.Suppressed));
        Assert.Equal(
            [new TierCountResponse("band_high", 11, false), new TierCountResponse("band_low", 5, false), new TierCountResponse("band_mid", 2, false)],
            distribution.Tiers);
    }

    [Fact]
    public async Task Small_tiers_are_suppressed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();

        var distribution = Assert.Single((await ReadAsync(client, $"?waveId={api.WaveOne.Id.Value}", cancellationToken)).Distributions);

        Assert.Equal(((int?)18, false), (distribution.Total, distribution.Suppressed));
        Assert.Equal(
            [new TierCountResponse("band_high", 11, false), new TierCountResponse("band_low", null, true), new TierCountResponse("band_mid", null, true)],
            distribution.Tiers);
    }

    [Fact]
    public async Task A_total_under_the_minimum_hides_the_whole_distribution_including_which_tiers_occur()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var strict = Factory.WithWebHostBuilder(builder => builder.UseSetting("Analytics:MinimumGroupSize", "20"));
        using var client = Researcher(strict, ApiClients.NewSubject("researcher"));

        var distribution = Assert.Single((await ReadAsync(client, $"?waveId={api.WaveOne.Id.Value}", cancellationToken)).Distributions);

        Assert.Equal(((int?)null, true), (distribution.Total, distribution.Suppressed));
        Assert.Empty(distribution.Tiers);
    }

    [Fact]
    public async Task A_wave_without_scores_has_no_distribution_and_each_read_is_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("researcher");
        using var client = Factory.AsResearcher(subject);

        var read = await ReadAsync(client, $"?waveId={api.WaveTwo.Id.Value}", cancellationToken);

        Assert.Empty(read.Distributions);
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.score_distributions.read" && audit.ActorId == subject, cancellationToken)));
    }

    [Fact]
    public async Task A_household_that_withdrew_from_the_study_is_left_out()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var unsuppressed = Factory.WithWebHostBuilder(builder => builder.UseSetting("Analytics:MinimumGroupSize", "1"));
        using var client = Researcher(unsuppressed, ApiClients.NewSubject("researcher"));

        var read = await ReadAsync(client, $"?waveId={api.WaveThree.Id.Value}", cancellationToken);

        Assert.Empty(read.Distributions);
    }

    [Theory]
    [InlineData(Roles.ProgramAdministrator, HttpStatusCode.Forbidden)]
    [InlineData(Roles.SystemAdministrator, HttpStatusCode.Forbidden)]
    [InlineData(Roles.SupportCoordinator, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Participant, HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task Only_researchers_read_score_distributions(string? role, HttpStatusCode expected)
    {
        using var client = role is null ? Factory.As(null) : Factory.As(ApiClients.NewSubject("caller"), role);

        using var response = await client.GetAsync("/api/v1/admin/analytics/score-distributions", TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("?waveId=00000000-0000-0000-0000-000000000000")]
    [InlineData("?waveId=not-a-uuid")]
    public async Task An_invalid_wave_returns_400_keyed_by_the_wave(string query)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();

        using var response = await client.GetAsync($"/api/v1/admin/analytics/score-distributions{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("waveId", await response.ErrorKeysAsync(cancellationToken));
    }
}
