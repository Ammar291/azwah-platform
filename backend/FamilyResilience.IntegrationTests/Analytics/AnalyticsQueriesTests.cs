using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Analytics;

/// <summary>
/// The aggregate queries count people as well as rows (D-58). The database is shared, so each test reads only the wave,
/// version and rule set it created.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AnalyticsQueriesTests(DatabaseFixture fixture)
{
    private async Task<T> QueryAsync<T>(Func<IAnalyticsQueries, Task<T>> read)
    {
        await using var scope = fixture.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<IAnalyticsQueries>());
    }

    /// <summary>Starts, answers, submits and scores the assignment with <paramref name="tier"/>, each in its own commit.</summary>
    private async Task CompleteAndScoreAsync(
        AssessmentAssignment assignment, InstrumentVersion version, RuleSet scoring, string tier, CancellationToken cancellationToken)
    {
        var sessionId = await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                var started = TestData.Ok(AssessmentSession.Start(tracked, TestData.Now));
                foreach (var item in version.GetItems())
                {
                    TestData.Ok(started.Answer(version, item.Id, item.Options[0].Id, TestData.Now));
                }

                services.GetRequiredService<IAssessmentSessionRepository>().Add(started);
                return started.Id;
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                TestData.Ok(session.Submit(version, tracked, TestData.Now));
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                services.GetRequiredService<IScoreResultRepository>().Add(TestData.Ok(ScoreResult.Record(
                    session, scoring, TestData.SyntheticComputation(session, tier), TestData.Now.AddMinutes(1))));
            },
            cancellationToken);
    }

    [Fact]
    public async Task One_member_holding_three_assignments_is_three_assignments_and_one_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var wave = await fixture.OpenWaveAsync(cancellationToken);
        var scoring = await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken);
        var household = await fixture.SavedHouseholdAsync(cancellationToken, TestData.Head(), TestData.Spouse());
        var (head, spouse) = (household.Members[0], household.Members[1]);
        var whole = await fixture.AssignmentAsync(head, version, cancellationToken, wave: wave);
        await fixture.AssignmentAsync(head, version, cancellationToken, version.Sections[0].Id, wave);
        await fixture.AssignmentAsync(head, version, cancellationToken, version.Sections[1].Id, wave);
        await fixture.AssignmentAsync(spouse, version, cancellationToken, wave: wave);
        await CompleteAndScoreAsync(whole, version, scoring, "band_x", cancellationToken);

        var byBirthMonth = await QueryAsync(queries => queries.AssignmentsByBirthMonthAsync(wave.Id, cancellationToken));
        var byEmployment = await QueryAsync(queries => queries.AssignmentsByEmploymentAsync(wave.Id, cancellationToken));

        Assert.Equal(
            [
                new BirthMonthAssignmentCount(head.BirthMonth.Year, head.BirthMonth.Month, 3, 1, 1, 1),
                new BirthMonthAssignmentCount(spouse.BirthMonth.Year, spouse.BirthMonth.Month, 1, 0, 1, 0),
            ],
            byBirthMonth.OrderBy(row => row.BirthYear));
        Assert.Equal(new EmploymentAssignmentCount(EmploymentStatus.Employed, 4, 1, 2, 1), Assert.Single(byEmployment));
    }

    [Fact]
    public async Task One_member_scored_in_two_waves_is_two_results_and_one_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var scoring = await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken);
        var (first, second) = (await fixture.OpenWaveAsync(cancellationToken), await fixture.OpenWaveAsync(cancellationToken));
        var twice = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var once = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var research = await fixture.ApprovedPolicyAsync(ConsentType.ResearchUse, cancellationToken);
        await fixture.GrantedConsentAsync(twice, research, cancellationToken);
        await fixture.GrantedConsentAsync(once, research, cancellationToken);
        await CompleteAndScoreAsync(await fixture.AssignmentAsync(twice, version, cancellationToken, wave: first), version, scoring, "band_x", cancellationToken);
        await CompleteAndScoreAsync(await fixture.AssignmentAsync(twice, version, cancellationToken, wave: second), version, scoring, "band_y", cancellationToken);
        await CompleteAndScoreAsync(await fixture.AssignmentAsync(once, version, cancellationToken, wave: first), version, scoring, "band_x", cancellationToken);
        await CompleteAndScoreAsync(await fixture.AssignmentAsync(once, version, cancellationToken, wave: second), version, scoring, "band_x", cancellationToken);

        var allWaves = (await QueryAsync(queries => queries.TierCountsAsync(null, cancellationToken)))
            .Where(row => row.RuleSetCode == scoring.Code)
            .OrderBy(row => row.TierCode, StringComparer.Ordinal)
            .Select(row => (row.TierCode, row.Count, row.Members, row.DistributionMembers));
        var firstWave = (await QueryAsync(queries => queries.TierCountsAsync(first.Id, cancellationToken)))
            .Where(row => row.RuleSetCode == scoring.Code)
            .Select(row => (row.TierCode, row.Count, row.Members, row.DistributionMembers));

        // Four results of two members: band_x holds three results of two members, band_y one.
        Assert.Equal([("band_x", 3, 2, 2), ("band_y", 1, 1, 2)], allWaves);
        Assert.Equal([("band_x", 2, 2, 2)], firstWave);
        Assert.True(Assert.Single(ScoreDistributions.Build(
            (await QueryAsync(queries => queries.TierCountsAsync(null, cancellationToken))).Where(row => row.RuleSetCode == scoring.Code).ToList(),
            minimum: 3)).Suppressed);
    }
}
