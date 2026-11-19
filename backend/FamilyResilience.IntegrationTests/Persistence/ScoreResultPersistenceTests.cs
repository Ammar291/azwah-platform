using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class ScoreResultPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_score_result_is_saved_with_its_contributions_and_categories_and_the_session_becomes_scored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (score, session, rules) = await fixture.ScoredSessionAsync(cancellationToken);

        var saved = await fixture.InScopeAsync(
            services => services.GetRequiredService<IScoreResultRepository>().ListForSessionAsync(session.Id, cancellationToken),
            cancellationToken);
        var savedSession = await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(candidate => candidate.Id == session.Id, cancellationToken));

        var result = Assert.Single(saved);
        Assert.Equal(score.Id, result.Id);
        Assert.Equal(rules.Id, result.RuleSetId);
        Assert.Equal("synthetic_policy", result.ScoringPolicyCode);
        Assert.Equal(1, result.ScoringAlgorithmVersion);
        Assert.Equal(ScoreStatus.Complete, result.Status);
        Assert.Equal(score.TotalScore, result.TotalScore);
        Assert.Equal(session.Responses.Count, result.Contributions.Count);
        Assert.All(result.Contributions, contribution => Assert.Equal("synthetic", contribution.ScoringCategory));
        Assert.Equal("synthetic", Assert.Single(result.CategoryScores).CategoryCode);
        Assert.Equal(SessionStatus.Scored, savedSession.Status);
    }

    [Fact]
    public async Task Scoring_twice_with_the_same_rule_set_returns_already_scored_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (score, session, rules) = await fixture.ScoredSessionAsync(cancellationToken);
        var firstScoredAt = (await fixture.QueryAsync(db =>
            db.AssessmentSessions.SingleAsync(candidate => candidate.Id == session.Id, cancellationToken))).ScoredAt;

        await using (var scope = fixture.CreateScope())
        {
            var tracked = await scope.ServiceProvider.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session.Id, cancellationToken);
            Assert.NotNull(tracked);
            var again = TestData.Ok(ScoreResult.Record(tracked, rules, TestData.SyntheticComputation(tracked), TestData.Now.AddMinutes(2)));
            scope.ServiceProvider.GetRequiredService<IScoreResultRepository>().Add(again);

            var committed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

            Assert.Equal(ScoringErrors.AlreadyScored, committed.Error);
        }

        var results = await fixture.QueryAsync(db => db.ScoreResults.Where(result => result.AssessmentSessionId == session.Id).ToListAsync(cancellationToken));
        var scoredAt = (await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(candidate => candidate.Id == session.Id, cancellationToken))).ScoredAt;
        Assert.Equal(score.Id, Assert.Single(results).Id);
        Assert.Equal(firstScoredAt, scoredAt);
    }
}
