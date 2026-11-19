using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Scoring;

/// <summary>"The latest result per session" must name exactly one result, also when two were computed at the same instant.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class HouseholdResultReadModelTests(DatabaseFixture fixture)
{
    private Task<ScoreResult> RecordAsync(
        AssessmentSessionId sessionId, RuleSet rules, string tier, DateTimeOffset computedAt, CancellationToken cancellationToken) =>
        fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var recorded = TestData.Ok(ScoreResult.Record(session, rules, TestData.SyntheticComputation(session, tier), computedAt));
                services.GetRequiredService<IScoreResultRepository>().Add(recorded);
                return recorded;
            },
            cancellationToken);

    private async Task<IReadOnlyList<HouseholdResultRowResponse>> RowsOfWaveAsync(WaveId waveId, CancellationToken cancellationToken)
    {
        await using var scope = fixture.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IHouseholdResultReadModel>()
            .ListLatestAsync(new HouseholdResultsRequest { WaveId = waveId.Value, PageSize = 100 }, cancellationToken);
        Assert.Equal(page.Items.Count, page.TotalCount);
        return page.Items;
    }

    [Fact]
    public async Task Two_results_of_one_session_computed_at_the_same_instant_give_one_latest_result()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, assignment, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        var computedAt = TestData.Now.AddMinutes(1);
        var first = await RecordAsync(
            session.Id, await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken), "tier_first", computedAt, cancellationToken);
        var second = await RecordAsync(
            session.Id, await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken), "tier_second", computedAt, cancellationToken);

        var rows = await RowsOfWaveAsync(assignment.WaveId, cancellationToken);

        // Both were computed at the same instant, so the later row (the greater version-7 identifier) is the latest.
        var saved = await fixture.QueryAsync(db => db.ScoreResults.AsNoTracking()
            .Where(result => result.AssessmentSessionId == session.Id)
            .OrderBy(result => result.Id)
            .Select(result => new { result.Id, result.ComputedAt, result.TierCode })
            .ToListAsync(cancellationToken));
        Assert.Equal([first.Id, second.Id], saved.Select(result => result.Id));
        Assert.All(saved, result => Assert.Equal(computedAt, result.ComputedAt));
        var row = Assert.Single(rows);
        Assert.Equal((session.Id.Value, "tier_second", computedAt), (row.SessionId, row.TierCode, row.ComputedAt));
    }

    [Fact]
    public async Task A_later_result_still_replaces_an_earlier_one_whatever_their_identifiers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, assignment, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        await RecordAsync(
            session.Id, await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken), "tier_first", TestData.Now.AddMinutes(1), cancellationToken);
        await RecordAsync(
            session.Id, await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken), "tier_second", TestData.Now.AddMinutes(2), cancellationToken);

        var row = Assert.Single(await RowsOfWaveAsync(assignment.WaveId, cancellationToken));

        Assert.Equal(("tier_second", TestData.Now.AddMinutes(2)), (row.TierCode, row.ComputedAt));
    }
}
