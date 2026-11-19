using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// Start, answer, submit and read the result end to end. Section A is scored (lowest answers give band_low, which the
/// approved referral rules route to synthetic_support); section B has no approved scoring rule set.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class SubmissionJourneyTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private Task<Enrolment> EnrolAsync(CancellationToken cancellationToken) =>
        Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));

    private Task<List<SupportRecommendation>> RecommendationsAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Factory.QueryAsync(db => db.Set<SupportRecommendation>()
            .Where(recommendation => recommendation.AssessmentSessionId == AssessmentSessionId.From(sessionId))
            .ToListAsync(cancellationToken));

    [Fact]
    public async Task Submitting_scores_the_session_and_records_a_recommendation_without_a_referral_when_no_referral_consent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);

        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var result = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var submission = await submitted.ReadAsync<SubmissionResponse>(cancellationToken);
        Assert.Equal((SubmissionOutcome.Submitted, SessionStatus.Scored, TestData.Now), (submission.Outcome, submission.Status, submission.SubmittedAt));
        var scored = await result.ReadAsync<SessionResultResponse>(cancellationToken);
        Assert.Equal(
            (ResultState.Complete, (decimal?)2m, "band_low", ScoreVisibility.Visible),
            (scored.State, scored.TotalScore, scored.TierCode, scored.ScoreVisibility));
        var recommendation = Assert.Single(await RecommendationsAsync(sessionId, cancellationToken));
        Assert.Equal(("synthetic_support", (SupportReferralId?)null), (recommendation.CategoryCode, recommendation.SupportReferralId));
        Assert.Equal(0, await Factory.QueryAsync(db => db.SupportReferrals.CountAsync(
            referral => referral.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken)));
    }

    [Fact]
    public async Task With_referral_consent_the_routing_creates_a_referral_that_a_withdrawal_cancels()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var consent = await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Referral, cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);

        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var referral = await Factory.QueryAsync(db => db.SupportReferrals.SingleAsync(
            candidate => candidate.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken));
        Assert.Equal((ReferralStatus.Open, "synthetic_support", ConsentRecordId.From(consent.Id)), (referral.Status, referral.CategoryCode, referral.ConsentRecordId));
        Assert.Equal(referral.Id, Assert.Single(await RecommendationsAsync(sessionId, cancellationToken)).SupportReferralId);
        Assert.Single(await Factory.OutboxMessagesAsync(
            "referrals.support_referral_created.v1", "supportReferralId", referral.Id.Value, cancellationToken));

        using var withdrawn = await enrolment.Client.PostJsonAsync(
            "/api/v1/consent", new { memberId = enrolment.Self, policyVersionId = consent.PolicyVersionId, decision = "withdraw" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        var cancelled = await Factory.QueryAsync(db => db.SupportReferrals.SingleAsync(candidate => candidate.Id == referral.Id, cancellationToken));
        Assert.Equal((ReferralStatus.Cancelled, RecordConsentDecision.ReferralCancelledResolution), (cancelled.Status, cancelled.ResolutionCode));
    }

    [Fact]
    public async Task Only_the_head_of_household_gets_the_score_so_a_manager_answering_for_the_head_sees_none()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(self: false), Rosters.Spouse(self: true));
        var head = enrolment.Members.Single(member => !member.IsCaller).Id;
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, head, "A", 0, cancellationToken);

        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var result = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);
        using var history = await enrolment.Client.GetAsync("/api/v1/me/assessment-history", cancellationToken);

        Assert.Equal(SessionStatus.Scored, (await submitted.ReadAsync<SubmissionResponse>(cancellationToken)).Status);
        var withheld = await result.ReadAsync<SessionResultResponse>(cancellationToken);
        Assert.Equal(
            (ResultState.Complete, (decimal?)null, (string?)null, ScoreVisibility.HeadOfHouseholdOnly),
            (withheld.State, withheld.TotalScore, withheld.TierCode, withheld.ScoreVisibility));
        Assert.Empty(withheld.CategoryScores);
        Assert.NotNull(withheld.ComputedAt);
        var item = Assert.Single((await history.ReadAsync<PagedResponse<AssessmentHistoryItemResponse>>(cancellationToken)).Items);
        Assert.Equal((sessionId, SessionStatus.Scored, (string?)null), (item.SessionId, item.Status, item.TierCode));
        Assert.Single(await RecommendationsAsync(sessionId, cancellationToken));
    }

    [Fact]
    public async Task A_tier_with_no_route_records_no_recommendation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 2, cancellationToken);

        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var result = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);

        Assert.Equal("band_high", (await result.ReadAsync<SessionResultResponse>(cancellationToken)).TierCode);
        Assert.Empty(await RecommendationsAsync(sessionId, cancellationToken));
    }

    [Fact]
    public async Task Without_an_approved_scoring_rule_set_the_session_is_submitted_and_its_result_is_pending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var parent = enrolment.Aged(68);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, parent, "B", 0, cancellationToken);

        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        using var result = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken);

        Assert.Equal(SessionStatus.Submitted, (await submitted.ReadAsync<SubmissionResponse>(cancellationToken)).Status);
        var pending = await result.ReadAsync<SessionResultResponse>(cancellationToken);
        Assert.Equal((ResultState.Pending, (string?)null, SessionStatus.Submitted), (pending.State, pending.TierCode, pending.SessionStatus));
    }

    [Fact]
    public async Task Starting_needs_participation_consent_and_only_one_live_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var assignmentId = await Journeys.AssignmentAsync(enrolment, enrolment.Self, "A", cancellationToken);

        using var withoutConsent = await enrolment.Client.PostJsonAsync($"/api/v1/assignments/{assignmentId}/sessions", new { }, cancellationToken);
        await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Participation, cancellationToken);
        var session = await Journeys.StartAsync(enrolment.Client, assignmentId, cancellationToken);
        using var twice = await enrolment.Client.PostJsonAsync($"/api/v1/assignments/{assignmentId}/sessions", new { }, cancellationToken);
        using var assignment = await enrolment.Client.GetAsync($"/api/v1/assignments/{assignmentId}", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, withoutConsent.StatusCode);
        Assert.Equal("sessions.participation_consent_required", await withoutConsent.ProblemCodeAsync(cancellationToken));
        Assert.Equal((2, 0, "A"), (session.RequiredCount, session.AnsweredCount, session.SectionCode));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("sessions.session_already_exists", await twice.ProblemCodeAsync(cancellationToken));
        Assert.Equal(session.Id, (await assignment.ReadAsync<ParticipantAssignmentResponse>(cancellationToken)).ActiveSessionId);
    }

    [Fact]
    public async Task Answers_are_validated_against_the_sessions_version_and_module()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Participation, cancellationToken);
        var session = await Journeys.StartAsync(
            enrolment.Client, await Journeys.AssignmentAsync(enrolment, enrolment.Self, "A", cancellationToken), cancellationToken);
        var sectionA = api.Section("A").Items.OrderBy(item => item.DisplayOrder).ToList();
        var sectionB = api.Section("B").Items.OrderBy(item => item.DisplayOrder).ToList();
        var url = $"/api/v1/sessions/{session.Id}/responses";

        using var empty = await enrolment.Client.PutJsonAsync(url, new { responses = Array.Empty<object>() }, cancellationToken);
        using var duplicate = await enrolment.Client.PutJsonAsync(url, new
        {
            responses = new[]
            {
                new { itemId = sectionA[0].Id.Value, optionId = sectionA[0].Options[0].Id.Value },
                new { itemId = sectionA[0].Id.Value, optionId = sectionA[0].Options[1].Id.Value },
            },
        }, cancellationToken);
        using var tooMany = await enrolment.Client.PutJsonAsync(url, new
        {
            responses = Enumerable.Range(0, 201).Select(_ => new { itemId = Guid.NewGuid(), optionId = Guid.NewGuid() }).ToArray(),
        }, cancellationToken);
        using var otherModule = await enrolment.Client.PutJsonAsync(url, new
        {
            responses = new[] { new { itemId = sectionB[0].Id.Value, optionId = sectionB[0].Options[0].Id.Value } },
        }, cancellationToken);
        using var otherItemsOption = await enrolment.Client.PutJsonAsync(url, new
        {
            responses = new[] { new { itemId = sectionA[0].Id.Value, optionId = sectionA[1].Options[0].Id.Value } },
        }, cancellationToken);
        using var unknownItem = await enrolment.Client.PutJsonAsync(url, new
        {
            responses = new[] { new { itemId = Guid.NewGuid(), optionId = sectionA[0].Options[0].Id.Value } },
        }, cancellationToken);

        Assert.Contains("responses", await empty.ErrorKeysAsync(cancellationToken));
        Assert.Contains("responses", await duplicate.ErrorKeysAsync(cancellationToken));
        Assert.Contains("responses", await tooMany.ErrorKeysAsync(cancellationToken));
        Assert.Equal("sessions.item_not_in_section", await otherModule.ProblemCodeAsync(cancellationToken));
        Assert.Equal("sessions.option_not_in_item", await otherItemsOption.ProblemCodeAsync(cancellationToken));
        Assert.Equal("sessions.item_not_in_version", await unknownItem.ProblemCodeAsync(cancellationToken));
        Assert.All([otherModule, otherItemsOption, unknownItem], response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        using var answers = await enrolment.Client.GetAsync(url, cancellationToken);
        Assert.Empty((await answers.ReadAsync<SessionAnswersResponse>(cancellationToken)).Responses);
    }

    [Fact]
    public async Task Saved_answers_read_back_in_item_order_and_each_read_is_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 1, cancellationToken);

        using var response = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken);
        using var session = await enrolment.Client.GetAsync($"/api/v1/sessions/{sessionId}", cancellationToken);

        var answers = await response.ReadAsync<SessionAnswersResponse>(cancellationToken);
        Assert.Equal(
            api.Section("A").Items.OrderBy(item => item.DisplayOrder).Select(item => item.Id.Value),
            answers.Responses.Select(answer => answer.ItemId));
        Assert.Equal(2, (await session.ReadAsync<SessionResponse>(cancellationToken)).AnsweredCount);
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "sessions.responses.read" && audit.ResourceId == sessionId.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task Submitting_needs_a_valid_idempotency_key_and_a_retry_returns_the_same_outcome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        var otherSession = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Aged(68), "B", 0, cancellationToken);
        var key = Guid.NewGuid();

        using var missing = await enrolment.Client.PostJsonAsync($"/api/v1/sessions/{sessionId}/submit", new { }, cancellationToken);
        using var malformed = await enrolment.Client.SendAsync(MalformedKey(sessionId), cancellationToken);
        using var first = await Journeys.SubmitAsync(enrolment.Client, sessionId, key, cancellationToken);
        using var retry = await Journeys.SubmitAsync(enrolment.Client, sessionId, key, cancellationToken);
        using var reused = await Journeys.SubmitAsync(enrolment.Client, otherSession, key, cancellationToken);

        Assert.Contains("idempotencyKey", await missing.ErrorKeysAsync(cancellationToken));
        Assert.Contains("idempotencyKey", await malformed.ErrorKeysAsync(cancellationToken));
        Assert.Equal(await first.Content.ReadAsStringAsync(cancellationToken), await retry.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("requests.idempotency_key_reused", await reused.ProblemCodeAsync(cancellationToken));
        Assert.Equal(1, await Factory.QueryAsync(db => db.ScoreResults.CountAsync(
            result => result.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken)));
    }

    private static HttpRequestMessage MalformedKey(Guid sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit");
        request.Headers.Add("Idempotency-Key", "not-a-uuid");
        return request;
    }

    [Fact]
    public async Task Submitting_before_every_required_item_is_answered_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Participation, cancellationToken);
        var session = await Journeys.StartAsync(
            enrolment.Client, await Journeys.AssignmentAsync(enrolment, enrolment.Self, "A", cancellationToken), cancellationToken);

        using var submitted = await Journeys.SubmitAsync(enrolment.Client, session.Id, Guid.NewGuid(), cancellationToken);
        using var result = await enrolment.Client.GetAsync($"/api/v1/sessions/{session.Id}/result", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, submitted.StatusCode);
        Assert.Equal("sessions.required_items_unanswered", await submitted.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("sessions.not_submitted", await result.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Another_familys_session_and_assignment_answer_404_and_staff_get_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await EnrolAsync(cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(owner, api, owner.Self, "A", 0, cancellationToken);
        var assignmentId = await Journeys.AssignmentAsync(owner, owner.Aged(68), "B", cancellationToken);
        using var stranger = await EnrolAsync(cancellationToken);
        using var staff = Factory.As(ApiClients.NewSubject("staff"), Application.Identity.Roles.SystemAdministrator);

        var attempts = new List<HttpResponseMessage>
        {
            await stranger.Client.GetAsync($"/api/v1/sessions/{sessionId}", cancellationToken),
            await stranger.Client.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken),
            await stranger.Client.PutJsonAsync($"/api/v1/sessions/{sessionId}/responses", new { responses = new[] { new { itemId = Guid.NewGuid(), optionId = Guid.NewGuid() } } }, cancellationToken),
            await Journeys.SubmitAsync(stranger.Client, sessionId, Guid.NewGuid(), cancellationToken),
            await stranger.Client.GetAsync($"/api/v1/sessions/{sessionId}/result", cancellationToken),
        };
        using var start = await stranger.Client.PostJsonAsync($"/api/v1/assignments/{assignmentId}/sessions", new { }, cancellationToken);
        using var asStaff = await staff.GetAsync($"/api/v1/sessions/{sessionId}/responses", cancellationToken);

        foreach (var attempt in attempts)
        {
            Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
            Assert.Equal("sessions.session_not_found", await attempt.ProblemCodeAsync(cancellationToken));
            attempt.Dispose();
        }

        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
        Assert.Equal("assignments.assignment_not_found", await start.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        var untouched = await Factory.QueryAsync(db => db.AssessmentSessions.SingleAsync(
            session => session.Id == AssessmentSessionId.From(sessionId), cancellationToken));
        Assert.Equal(SessionStatus.InProgress, untouched.Status);
    }

    [Fact]
    public async Task Assessment_history_lists_submitted_check_ins_with_their_tier()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var empty = await enrolment.Client.GetAsync("/api/v1/me/assessment-history", cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);

        using var history = await enrolment.Client.GetAsync("/api/v1/me/assessment-history", cancellationToken);

        Assert.Equal(0, (await empty.ReadAsync<PagedResponse<AssessmentHistoryItemResponse>>(cancellationToken)).TotalCount);
        var item = Assert.Single((await history.ReadAsync<PagedResponse<AssessmentHistoryItemResponse>>(cancellationToken)).Items);
        Assert.Equal(
            (sessionId, api.Instrument.Code, "A", api.Wave.Code, SessionStatus.Scored, "band_low"),
            (item.SessionId, item.InstrumentCode, item.SectionCode, item.WaveCode, item.Status, item.TierCode));
        var reads = await Factory.QueryAsync(db => db.AuditEvents
            .Where(audit => audit.Action == "sessions.history.read" && audit.ActorId == enrolment.Subject)
            .ToListAsync(cancellationToken));
        Assert.Equal(2, reads.Count);
        Assert.All(reads, read => Assert.Equal(("assessment_history", (string?)null), (read.ResourceType, read.ResourceId)));
    }
}
