using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Scoring;

[Collection(DatabaseCollection.Name)]
public sealed class ScoreSessionTests(DatabaseFixture fixture)
{
    private const string WholeVersion =
        """{"schema":"scoring.v1","policy":"summed_item_values","algorithmVersion":1,"parameters":{"reverseScoring":"mirror_scale","unanswered":"reject","total":true}}""";

    /// <summary>Approves a scoring rule set inside the test database only, bypassing the proposal validator on purpose.</summary>
    private async Task<RuleSet> ApproveScoringAsync(InstrumentVersion version, string definition, CancellationToken cancellationToken)
    {
        var rules = TestData.Ok(RuleSet.Propose(RuleSetKind.Scoring, TestData.UniqueCode("scoring"), 1, version, definition, TestData.Now));
        TestData.Ok(rules.Approve(TestData.TestApproval(), version, TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
                return Task.CompletedTask;
            },
            cancellationToken);
        return rules;
    }

    private async Task<Result<ScoreSessionOutcome>> ScoreAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ScoreSession>().HandleAsync(sessionId.Value, cancellationToken);
    }

    private async Task AssertUnscoredAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken)
    {
        var session = await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(candidate => candidate.Id == sessionId, cancellationToken));
        Assert.Equal(SessionStatus.Submitted, session.Status);
        Assert.Equal(0, await fixture.QueryAsync(db => db.ScoreResults.CountAsync(result => result.AssessmentSessionId == sessionId, cancellationToken)));
    }

    [Fact]
    public async Task Without_an_approved_scoring_rule_set_scoring_is_refused_and_nothing_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);

        Assert.Equal(ScoringErrors.PolicyNotApproved, (await ScoreAsync(session.Id, cancellationToken)).Error);
        await AssertUnscoredAsync(session.Id, cancellationToken);
    }

    [Fact]
    public async Task Scoring_records_the_result_and_marks_the_session_scored_in_one_commit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        var rules = await ApproveScoringAsync(version, WholeVersion, cancellationToken);

        var outcome = TestData.Ok(await ScoreAsync(session.Id, cancellationToken));

        Assert.True(outcome.Created);
        Assert.Equal((ScoreStatus.Complete, 4m, rules.Id.Value), (outcome.Result.Status, outcome.Result.TotalScore, outcome.Result.RuleSetId));
        Assert.Equal(("summed_item_values", 1), (outcome.Result.PolicyCode, outcome.Result.AlgorithmVersion));
        Assert.Equal(4, outcome.Result.ContributionCount);
        var saved = await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(candidate => candidate.Id == session.Id, cancellationToken));
        Assert.Equal(SessionStatus.Scored, saved.Status);
        Assert.Single(await fixture.OutboxMessagesAsync("scoring.score_result_recorded.v1", "scoreResultId", outcome.Result.Id, cancellationToken));
        Assert.Equal(1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "scoring.score_result.recorded" && audit.ResourceId == outcome.Result.Id.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task Scoring_again_returns_the_same_result_without_a_second_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(version, WholeVersion, cancellationToken);
        var first = TestData.Ok(await ScoreAsync(session.Id, cancellationToken));

        var second = TestData.Ok(await ScoreAsync(session.Id, cancellationToken));

        Assert.False(second.Created);
        Assert.Equal(first.Result.Id, second.Result.Id);
        Assert.Equal(1, await fixture.QueryAsync(db => db.ScoreResults.CountAsync(result => result.AssessmentSessionId == session.Id, cancellationToken)));
        Assert.Equal(1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "scoring.score_results.read" && audit.ResourceType == "assessment_session"
                && audit.ResourceId == session.Id.Value.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task An_existing_result_is_not_returned_when_its_read_cannot_be_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(version, WholeVersion, cancellationToken);
        TestData.Ok(await ScoreAsync(session.Id, cancellationToken));

        await using var scope = fixture.CreateScope();
        var refused = await ActivatorUtilities.CreateInstance<ScoreSession>(scope.ServiceProvider, new RefusingAuditLog())
            .HandleAsync(session.Id.Value, cancellationToken);

        Assert.Equal(AuditErrors.ActorIdRequired, refused.Error);
    }

    [Fact]
    public async Task An_unsupported_policy_is_refused_and_nothing_is_saved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(version, WholeVersion.Replace("summed_item_values", "weighted_sum", StringComparison.Ordinal), cancellationToken);

        Assert.Equal(ScoringErrors.PolicyUnsupported, (await ScoreAsync(session.Id, cancellationToken)).Error);
        await AssertUnscoredAsync(session.Id, cancellationToken);
    }

    [Fact]
    public async Task A_strategy_failure_saves_no_partial_result()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        // Four items valued 1–3: totals 4–12. These synthetic bands stop at 11, so the strategy refuses them.
        await ApproveScoringAsync(
            version,
            WholeVersion.Replace("\"total\":true", "\"total\":true,\"tierBands\":[{\"code\":\"band_all\",\"min\":4,\"max\":11}]", StringComparison.Ordinal),
            cancellationToken);

        Assert.Equal(ScoringErrors.TierBandsInvalid.Code, (await ScoreAsync(session.Id, cancellationToken)).Error?.Code);
        await AssertUnscoredAsync(session.Id, cancellationToken);
    }

    [Fact]
    public async Task Two_approved_rule_sets_for_the_same_scope_are_ambiguous_and_nothing_is_saved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(version, WholeVersion, cancellationToken);
        await ApproveScoringAsync(version, WholeVersion, cancellationToken);

        Assert.Equal(ScoringErrors.AmbiguousRules, (await ScoreAsync(session.Id, cancellationToken)).Error);
        await AssertUnscoredAsync(session.Id, cancellationToken);
    }

    [Fact]
    public async Task An_in_progress_session_cannot_be_scored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, _, _) = await fixture.StartedSessionAsync(cancellationToken);

        Assert.Equal(ScoringErrors.SessionNotSubmitted, (await ScoreAsync(session.Id, cancellationToken)).Error);
    }

    private static string ForSection(string sectionCode) =>
        WholeVersion.Replace("{\"schema\"", $"{{\"target\":{{\"sectionCode\":\"{sectionCode}\"}},\"schema\"", StringComparison.Ordinal);

    [Fact]
    public async Task A_section_session_is_scored_with_the_rule_set_that_targets_its_section()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken, "A");
        var forA = await ApproveScoringAsync(version, ForSection("A"), cancellationToken);
        await ApproveScoringAsync(version, ForSection("B"), cancellationToken);

        var outcome = TestData.Ok(await ScoreAsync(session.Id, cancellationToken));

        Assert.True(outcome.Created);
        Assert.Equal((forA.Id.Value, 2m, 2), (outcome.Result.RuleSetId, outcome.Result.TotalScore, outcome.Result.ContributionCount));
    }

    [Fact]
    public async Task A_whole_version_session_is_not_scored_by_a_section_rule_set()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(version, ForSection("A"), cancellationToken);

        Assert.Equal(ScoringErrors.PolicyNotApproved, (await ScoreAsync(session.Id, cancellationToken)).Error);
        await AssertUnscoredAsync(session.Id, cancellationToken);
    }

    [Fact]
    public async Task Listed_results_count_their_contributions_but_never_return_the_answers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (score, session, _) = await fixture.ScoredSessionAsync(cancellationToken);

        await using var scope = fixture.CreateScope();
        var listed = TestData.Ok(await scope.ServiceProvider.GetRequiredService<ListScoreResults>().HandleAsync(session.Id.Value, cancellationToken));

        var result = Assert.Single(listed);
        Assert.Equal((score.Id.Value, session.Responses.Count), (result.Id, result.ContributionCount));
        Assert.Null(typeof(ScoreResultResponse).GetProperty("Contributions"));
    }

    [Fact]
    public async Task Listing_results_writes_exactly_one_read_audit_row_for_the_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        await ApproveScoringAsync(version, WholeVersion, cancellationToken);
        TestData.Ok(await ScoreAsync(session.Id, cancellationToken));

        await using (var scope = fixture.CreateScope())
        {
            TestData.Ok(await scope.ServiceProvider.GetRequiredService<ListScoreResults>().HandleAsync(session.Id.Value, cancellationToken));
        }

        Assert.Equal(1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "scoring.score_results.read" && audit.ResourceType == "assessment_session"
                && audit.ResourceId == session.Id.Value.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task An_unknown_session_returns_session_not_found() =>
        Assert.Equal(SessionErrors.SessionNotFound, (await ScoreAsync(AssessmentSessionId.New(), TestContext.Current.CancellationToken)).Error);

    /// <summary>An audit log that can never record, standing in for a failed audit write.</summary>
    private sealed class RefusingAuditLog : IAuditLog
    {
        public Result Stage(AuditRecord record) => Result.Failure(AuditErrors.ActorIdRequired);

        public Task<Result> RecordAsync(AuditRecord record, CancellationToken cancellationToken = default) => Task.FromResult(Stage(record));
    }
}
