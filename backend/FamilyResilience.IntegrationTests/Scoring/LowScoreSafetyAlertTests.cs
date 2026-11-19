using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Scoring;

/// <summary>
/// A household-assessment score in the lowest band raises a safety alert once the outbox delivers
/// <c>scoring.score_result_recorded.v1</c> (D-136). The seeded participant's own approved synthetic version gets its own
/// approved rule set under the code the default <c>Safeguarding:ScoreAlerts:Tiers</c> lists, so the shipped default is
/// what decides; nothing here adds eligibility rules or waves to the shared database.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LowScoreSafetyAlertTests(ApiFactory factory)
{
    private const string MessageType = "scoring.score_result_recorded.v1";

    /// <summary>The household assessment's scoring rule-set code, as the default configuration names it.</summary>
    private const string HouseholdAssessmentScoring = "household_assessment_total";

    /// <summary>Synthetic bands over the seeded version's four items valued 1–3 (totals 4–12); not the real bands.</summary>
    private const string BandedScoring =
        """{"schema":"scoring.v1","policy":"summed_item_values","algorithmVersion":1,"parameters":{"reverseScoring":"mirror_scale","unanswered":"reject","total":true,"tierBands":[{"code":"vulnerable","min":4,"max":7},{"code":"band_high","min":8,"max":12}]}}""";

    private static string Describe(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Describe(exception.InnerException)}";

    /// <summary>Approves a scoring rule set for the participant's own version, with a version number no other test uses.</summary>
    private async Task ApproveScoringAsync(InstrumentVersion version, CancellationToken cancellationToken)
    {
        var rules = TestData.Ok(RuleSet.Propose(
            RuleSetKind.Scoring, HouseholdAssessmentScoring, TestData.NextVersionNumber(), version, BandedScoring, TestData.Now));
        TestData.Ok(rules.Approve(TestData.TestApproval(), version, TestData.Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    /// <summary>Starts, answers (every item with its lowest option) and submits a session through the API.</summary>
    private static async Task<Guid> SubmittedAsync(SeededParticipant participant, CancellationToken cancellationToken)
    {
        using var started = await participant.Client.PostJsonAsync($"/api/v1/assignments/{participant.AssignmentId}/sessions", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var session = await started.ReadAsync<SessionResponse>(cancellationToken);
        using var answered = await participant.Client.PutJsonAsync($"/api/v1/sessions/{session.Id}/responses", participant.Answers(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{session.Id}/submit");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var submitted = await participant.Client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(SessionStatus.Scored, (await submitted.ReadAsync<SubmissionResponse>(cancellationToken)).Status);
        return session.Id;
    }

    /// <summary>
    /// The API with only the score handler as registered by the application, so draining the shared database's outbox
    /// here never runs other tests' exports.
    /// </summary>
    private WebApplicationFactory<Program> ScoreHandlerOnly() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            foreach (var other in services
                         .Where(descriptor => descriptor.ServiceType == typeof(IOutboxMessageHandler)
                             && descriptor.ImplementationType != typeof(ScoreResultRecordedSafetyHandler))
                         .ToList())
            {
                services.Remove(other);
            }
        }));

    /// <summary>Runs the outbox until no due message of a handled type is left, as the dispatcher would.</summary>
    private static async Task DrainOutboxAsync(WebApplicationFactory<Program> host, CancellationToken cancellationToken)
    {
        for (var round = 0; round < 500; round++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessNextAsync(cancellationToken))
            {
                return;
            }
        }

        Assert.Fail("The outbox did not drain.");
    }

    private Task<List<SafetyAlert>> AlertsForAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken) =>
        factory.QueryAsync(db => db.SafetyAlerts.AsNoTracking()
            .Where(alert => alert.AssessmentSessionId == sessionId)
            .ToListAsync(cancellationToken));

    [Fact]
    public async Task Submitting_a_session_scored_vulnerable_raises_one_high_alert_after_the_outbox_runs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = await SeededParticipant.CreateAsync(factory, cancellationToken);
        await ApproveScoringAsync(participant.Version, cancellationToken);
        var sessionId = AssessmentSessionId.From(await SubmittedAsync(participant, cancellationToken));
        var result = await factory.QueryAsync(db => db.ScoreResults.AsNoTracking()
            .SingleAsync(candidate => candidate.AssessmentSessionId == sessionId, cancellationToken));
        Assert.Equal((ScoreStatus.Complete, "vulnerable"), (result.Status, result.TierCode));
        Assert.Empty(await AlertsForAsync(sessionId, cancellationToken));

        await using var host = ScoreHandlerOnly();
        await DrainOutboxAsync(host, cancellationToken);

        var alert = Assert.Single(await AlertsForAsync(sessionId, cancellationToken));
        Assert.Null(alert.UserId);
        Assert.Equal(
            ((HouseholdId?)participant.Member.HouseholdId, (FamilyMemberId?)participant.Member.Id),
            (alert.HouseholdId, alert.MemberId));
        Assert.Equal(
            (SafetyAlertSource.HouseholdAssessment, SafetyConcern.LowResilienceScore, SafetySeverity.High, SafetyDetector.ScoreBand),
            (alert.Source, alert.Concern, alert.Severity, alert.Detector));
        Assert.Equal(("vulnerable", (string?)null, SafetyAlertStatus.Open, 1), (alert.SignalCode, alert.Language, alert.Status, alert.OccurrenceCount));
        var message = Assert.Single(await factory.OutboxMessagesAsync(MessageType, "scoreResultId", result.Id.Value, cancellationToken));
        Assert.Equal(OutboxMessageStatus.Processed, message.Status);
        var audit = Assert.Single(await factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(candidate => candidate.Action == RecordSafetySignal.Raised && candidate.ResourceId == alert.Id.Value.ToString())
            .ToListAsync(cancellationToken)));
        Assert.Equal((AuditActorType.System, (string?)null), (audit.ActorType, audit.ActorId));

        // A second delivery of the same message (after a crash, say) finds the session's alert and adds nothing.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetServices<IOutboxMessageHandler>().Single(handler => handler.Type == MessageType)
                .HandleAsync(new OutboxEnvelope(message.Id, message.Type, message.Payload, message.OccurredAt), cancellationToken);
        }

        Assert.Single(await AlertsForAsync(sessionId, cancellationToken));
    }

    [Fact]
    public async Task Demo_answers_for_a_low_result_are_scored_vulnerable_and_raise_the_same_alert_without_a_fault()
    {
        // D-130 meets D-136: a demonstration's "low" goes through the ordinary save, submit, scoring and outbox paths.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = await SeededParticipant.CreateAsync(factory, cancellationToken);
        await ApproveScoringAsync(participant.Version, cancellationToken);
        await using var host = ScoreHandlerOnly().WithWebHostBuilder(builder => builder.UseSetting(DemoToolsSettings.EnabledKey, "true"));
        using var client = host.As(participant.Subject, Roles.Participant);
        using var started = await client.PostJsonAsync($"/api/v1/assignments/{participant.AssignmentId}/sessions", new { }, cancellationToken);
        var session = await started.ReadAsync<SessionResponse>(cancellationToken);

        using var planned = await client.GetAsync($"/api/v1/sessions/{session.Id}/demo-answers?level=low", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, planned.StatusCode);
        var plan = await planned.ReadAsync<DemoAnswersResponse>(cancellationToken);
        using var answered = await client.PutJsonAsync($"/api/v1/sessions/{session.Id}/responses", new { responses = plan.Responses }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{session.Id}/submit");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var submitted = await client.SendAsync(request, cancellationToken);
        Assert.Equal(SessionStatus.Scored, (await submitted.ReadAsync<SubmissionResponse>(cancellationToken)).Status);

        var sessionId = AssessmentSessionId.From(session.Id);
        var result = await factory.QueryAsync(db => db.ScoreResults.AsNoTracking()
            .SingleAsync(candidate => candidate.AssessmentSessionId == sessionId, cancellationToken));
        Assert.Equal((ScoreStatus.Complete, "vulnerable"), (result.Status, result.TierCode));
        await DrainOutboxAsync(host, cancellationToken);

        var alert = Assert.Single(await AlertsForAsync(sessionId, cancellationToken));
        Assert.Equal((SafetyConcern.LowResilienceScore, SafetyDetector.ScoreBand), (alert.Concern, alert.Detector));
        var message = Assert.Single(await factory.OutboxMessagesAsync(MessageType, "scoreResultId", result.Id.Value, cancellationToken));
        Assert.Equal(OutboxMessageStatus.Processed, message.Status);
    }

    [Fact]
    public async Task The_facts_of_a_result_name_the_rule_set_the_assignment_member_and_their_household()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = await SeededParticipant.CreateAsync(factory, cancellationToken);
        await ApproveScoringAsync(participant.Version, cancellationToken);
        var sessionId = AssessmentSessionId.From(await SubmittedAsync(participant, cancellationToken));
        var resultId = await factory.QueryAsync(db => db.ScoreResults.AsNoTracking()
            .Where(candidate => candidate.AssessmentSessionId == sessionId)
            .Select(candidate => candidate.Id)
            .SingleAsync(cancellationToken));

        await using var scope = factory.Services.CreateAsyncScope();
        var facts = scope.ServiceProvider.GetRequiredService<IScoreResultFacts>();
        var found = await facts.GetAsync(resultId, cancellationToken);
        var missing = await facts.GetAsync(ScoreResultId.New(), cancellationToken);

        Assert.Equal(
            new ScoreResultFacts(
                HouseholdAssessmentScoring, ScoreStatus.Complete, "vulnerable", sessionId, participant.Member.HouseholdId, participant.Member.Id),
            found);
        Assert.Null(missing);
    }

    [Theory]
    [InlineData("Safeguarding:ScoreAlerts:Enabled", "Maybe", "enabled_invalid")]
    [InlineData("Safeguarding:ScoreAlerts:Tiers:Household-Assessment:0", "vulnerable", "rule_set_code_invalid")]
    [InlineData("Safeguarding:ScoreAlerts:Tiers:household_assessment_total:0", "Very Vulnerable", "tier_code_invalid")]
    public void The_api_refuses_to_start_with_unreadable_score_alert_settings(string key, string value, string problem)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, sources) =>
            sources.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        var message = Describe(error);
        Assert.Contains($"{ScoreAlertSettings.Section} is invalid: {problem}", message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, message, StringComparison.Ordinal);
    }
}
