using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Api;

/// <summary>
/// A submission whose scoring or routing fails leaves nothing behind: the session stays in progress, nothing is scored or
/// referred, and the idempotency key is not stored, so the client can retry once the rules are corrected.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SubmissionRollbackTests(ApiFactory factory)
{
    private const string WholeVersionScoring =
        """{"schema":"scoring.v1","policy":"summed_item_values","algorithmVersion":1,"parameters":{"reverseScoring":"mirror_scale","unanswered":"reject","total":true,"tierBands":[{"code":"band_low","min":4,"max":7},{"code":"band_high","min":8,"max":12}]}}""";

    private const string WholeVersionReferral =
        """{"schema":"referral.v1","routes":[{"categoryCode":"synthetic_support","when":{"tierCodeIn":["band_low"]}}]}""";

    private async Task ApproveAsync(RuleSetKind kind, InstrumentVersion version, string definition, CancellationToken cancellationToken)
    {
        var rules = TestData.Ok(RuleSet.Propose(kind, TestData.UniqueCode("rules"), 1, version, definition, TestData.Now));
        TestData.Ok(rules.Approve(TestData.TestApproval(), version, TestData.Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    private async Task<(Guid SessionId, SeededParticipant Participant)> AnsweredAsync(CancellationToken cancellationToken)
    {
        var participant = await SeededParticipant.CreateAsync(factory, cancellationToken);
        using var started = await participant.Client.PostJsonAsync($"/api/v1/assignments/{participant.AssignmentId}/sessions", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var session = await started.ReadAsync<SessionResponse>(cancellationToken);
        using var answered = await participant.Client.PutJsonAsync($"/api/v1/sessions/{session.Id}/responses", participant.Answers(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        return (session.Id, participant);
    }

    private async Task AssertNothingChangedAsync(Guid sessionId, Guid assignmentId, Guid key, CancellationToken cancellationToken)
    {
        var session = await factory.QueryAsync(db => db.AssessmentSessions.SingleAsync(
            candidate => candidate.Id == AssessmentSessionId.From(sessionId), cancellationToken));
        var assignment = await factory.QueryAsync(db => db.AssessmentAssignments.SingleAsync(
            candidate => candidate.Id == AssessmentAssignmentId.From(assignmentId), cancellationToken));
        Assert.Equal((SessionStatus.InProgress, (DateTimeOffset?)null), (session.Status, session.SubmittedAt));
        Assert.Equal(AssignmentStatus.InProgress, assignment.Status);
        Assert.Equal(0, await factory.QueryAsync(db => db.ScoreResults.CountAsync(
            result => result.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken)));
        Assert.Equal(0, await factory.QueryAsync(db => db.SupportReferrals.CountAsync(
            referral => referral.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken)));
        Assert.Equal(0, await factory.QueryAsync(db => db.Set<IdempotencyRecord>().CountAsync(record => record.Key == key, cancellationToken)));
        Assert.Equal(0, await factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "sessions.session.submitted" && audit.ResourceId == sessionId.ToString(), cancellationToken)));
        Assert.Empty(await factory.OutboxMessagesAsync("sessions.assessment_session_submitted.v1", "assessmentSessionId", sessionId, cancellationToken));
    }

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid sessionId, Guid key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit");
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request, cancellationToken);
    }

    [Fact]
    public async Task Ambiguous_scoring_rules_roll_the_whole_submission_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, participant) = await AnsweredAsync(cancellationToken);
        using var _ = participant;
        await ApproveAsync(RuleSetKind.Scoring, participant.Version, WholeVersionScoring, cancellationToken);
        await ApproveAsync(RuleSetKind.Scoring, participant.Version, WholeVersionScoring, cancellationToken);
        var key = Guid.NewGuid();

        using var response = await SubmitAsync(participant.Client, sessionId, key, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("scoring.ambiguous_rules", await response.ProblemCodeAsync(cancellationToken));
        await AssertNothingChangedAsync(sessionId, participant.AssignmentId, key, cancellationToken);
    }

    [Fact]
    public async Task Ambiguous_referral_rules_roll_back_the_score_and_the_submission()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, participant) = await AnsweredAsync(cancellationToken);
        using var _ = participant;
        await ApproveAsync(RuleSetKind.Scoring, participant.Version, WholeVersionScoring, cancellationToken);
        await ApproveAsync(RuleSetKind.Referral, participant.Version, WholeVersionReferral, cancellationToken);
        await ApproveAsync(RuleSetKind.Referral, participant.Version, WholeVersionReferral, cancellationToken);
        var key = Guid.NewGuid();

        using var response = await SubmitAsync(participant.Client, sessionId, key, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("referrals.ambiguous_rules", await response.ProblemCodeAsync(cancellationToken));
        await AssertNothingChangedAsync(sessionId, participant.AssignmentId, key, cancellationToken);
    }

    [Fact]
    public async Task Once_the_rules_are_unambiguous_the_same_key_submits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, participant) = await AnsweredAsync(cancellationToken);
        using var _ = participant;
        await ApproveAsync(RuleSetKind.Scoring, participant.Version, WholeVersionScoring, cancellationToken);
        await ApproveAsync(RuleSetKind.Referral, participant.Version, WholeVersionReferral, cancellationToken);
        var key = Guid.NewGuid();

        using var response = await SubmitAsync(participant.Client, sessionId, key, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var submission = await response.ReadAsync<SubmissionResponse>(cancellationToken);
        Assert.Equal(SessionStatus.Scored, submission.Status);
        Assert.Equal(1, await factory.QueryAsync(db => db.Set<Domain.Referrals.SupportRecommendation>().CountAsync(
            recommendation => recommendation.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken)));
    }
}
