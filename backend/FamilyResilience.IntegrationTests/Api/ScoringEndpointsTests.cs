using System.Net;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.IntegrationTests.Support;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class ScoringEndpointsTests(ApiFactory factory)
{
    private const string WholeVersion =
        """{"schema":"scoring.v1","policy":"summed_item_values","algorithmVersion":1,"parameters":{"reverseScoring":"mirror_scale","unanswered":"reject","total":true}}""";

    private async Task ApproveScoringAsync(InstrumentVersion version, CancellationToken cancellationToken, string definition = WholeVersion)
    {
        var rules = TestData.Ok(RuleSet.Propose(RuleSetKind.Scoring, TestData.UniqueCode("scoring"), 1, version, definition, TestData.Now));
        TestData.Ok(rules.Approve(TestData.TestApproval(), version, TestData.Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    [Fact]
    public async Task Scoring_returns_201_then_200_for_the_same_result_and_lists_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await factory.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(version, cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var first = await client.PostJsonAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score", new { }, cancellationToken);
        using var second = await client.PostJsonAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score", new { }, cancellationToken);
        using var listed = await client.GetAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score-results", cancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal($"/api/v1/assessment-sessions/{session.Id.Value}/score-results", first.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var result = await first.ReadAsync<ScoreResultResponse>(cancellationToken);
        Assert.Equal(result.Id, (await second.ReadAsync<ScoreResultResponse>(cancellationToken)).Id);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(result.Id, Assert.Single(await listed.ReadAsync<List<ScoreResultResponse>>(cancellationToken)).Id);
        Assert.Contains("\"status\":\"complete\"", await listed.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scoring_without_an_approved_policy_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, _, _) = await factory.SubmittedSessionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("scoring.policy_not_approved", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Scoring_a_session_that_is_not_submitted_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, _, _) = await factory.StartedSessionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("scoring.session_not_submitted", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Two_approved_rule_sets_for_the_same_scope_return_409_ambiguous_rules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await factory.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(version, cancellationToken);
        await ApproveScoringAsync(version, cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("scoring.ambiguous_rules", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Tier_bands_that_do_not_cover_the_totals_return_400_and_save_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await factory.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(
            version,
            cancellationToken,
            WholeVersion.Replace("\"total\":true", "\"total\":true,\"tierBands\":[{\"code\":\"band_all\",\"min\":4,\"max\":11}]", StringComparison.Ordinal));
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("scoring.tier_bands_invalid", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(0, await factory.QueryAsync(db => db.ScoreResults.CountAsync(result => result.AssessmentSessionId == session.Id, cancellationToken)));
        var saved = await factory.QueryAsync(db => db.AssessmentSessions.SingleAsync(candidate => candidate.Id == session.Id, cancellationToken));
        Assert.Equal(SessionStatus.Submitted, saved.Status);
    }

    [Fact]
    public async Task An_unknown_session_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var score = await client.PostJsonAsync($"/api/v1/assessment-sessions/{Guid.NewGuid()}/score", new { }, cancellationToken);
        using var list = await client.GetAsync($"/api/v1/assessment-sessions/{Guid.NewGuid()}/score-results", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, score.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal("sessions.session_not_found", await score.ProblemCodeAsync(cancellationToken));
        Assert.Equal("sessions.session_not_found", await list.ProblemCodeAsync(cancellationToken));
    }
}
